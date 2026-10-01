using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.HwProbe.FakeFfmpeg;

/// <summary>Impersonates ffmpeg, replaying the scenario named by <c>HWPROBE_FAKE_SCENARIO</c>.</summary>
internal static class Program
{
    /// <summary>Environment variable holding the scenario file path.</summary>
    internal const string ScenarioVariable = "HWPROBE_FAKE_SCENARIO";

    /// <summary>Environment variable marking a spawned grandchild, which only hangs.</summary>
    private const string RoleVariable = "HWPROBE_FAKE_ROLE";

    private const int FillChunkBytes = 64 * 1024;

    /// <summary>Runs one fake ffmpeg invocation.</summary>
    /// <param name="args">ffmpeg-style arguments, matched against the scenario's rules.</param>
    /// <returns>The scenario's exit code.</returns>
    private static async Task<int> Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable(RoleVariable) == "grandchild")
        {
            await Task.Delay(Timeout.Infinite);
        }

        var scenarioPath = Environment.GetEnvironmentVariable(ScenarioVariable)
            ?? throw new InvalidOperationException($"{ScenarioVariable} is not set.");
        var scenario = JsonSerializer.Deserialize(await File.ReadAllTextAsync(scenarioPath), FakeJsonContext.Default.FakeScenario)
            ?? throw new InvalidOperationException($"{scenarioPath} is empty.");
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(scenarioPath))!;

        var start = DateTimeOffset.UtcNow;
        var joined = string.Join(' ', args);
        var response = (scenario.Rules ?? []).FirstOrDefault(r => joined.Contains(r.Match, StringComparison.Ordinal))?.Response
            ?? scenario.Default
            ?? new FakeResponse();

        await PlayAsync(response, baseDirectory);

        if (scenario.LogFile is not null)
        {
            var entry = new FakeInvocation(joined, Environment.ProcessId, start, DateTimeOffset.UtcNow);
            var line = JsonSerializer.Serialize(entry, FakeJsonContext.Default.FakeInvocation) + "\n";
            await File.AppendAllTextAsync(scenario.LogFile, line);
        }

        if (response.Hang)
        {
            await Task.Delay(Timeout.Infinite);
        }

        return response.ExitCode;
    }

    /// <summary>Performs everything a response asks for except hanging and exiting.</summary>
    /// <param name="response">The selected response.</param>
    /// <param name="baseDirectory">Directory that relative file references resolve against.</param>
    /// <returns>A task that completes when the response has been played.</returns>
    private static async Task PlayAsync(FakeResponse response, string baseDirectory)
    {
        var pids = new List<int> { Environment.ProcessId };
        if (response.SpawnChild)
        {
            pids.Add(SpawnGrandchild());
        }

        if (response.PidFile is not null)
        {
            var lines = pids.Select(p => p.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await File.WriteAllLinesAsync(response.PidFile, lines);
        }

        await using var stdout = Console.OpenStandardOutput();
        await using var stderr = Console.OpenStandardError();

        foreach (var name in response.EchoEnv ?? [])
        {
            var value = Environment.GetEnvironmentVariable(name);
            await WriteAsync(stdout, value is null ? $"{name}!unset\n" : $"{name}={value}\n");
        }

        await WriteAsync(stdout, response.Stdout);
        await WriteAsync(stderr, response.Stderr);
        await WriteFileAsync(stdout, baseDirectory, response.StdoutFile);
        await WriteFileAsync(stderr, baseDirectory, response.StderrFile);
        await WriteFillAsync(stdout, stderr, response.StdoutFillBytes, response.StderrFillBytes);

        if (response.Frames is { } frames)
        {
            // Same shape as -progress pipe:1 output.
            await WriteAsync(stdout, $"frame={frames}\nfps=0.00\nprogress=end\n");
        }

        if (response.DelayMs > 0)
        {
            await Task.Delay(response.DelayMs);
        }
    }

    /// <summary>Starts a copy of this executable that hangs forever, inheriting our stdio pipes.</summary>
    /// <returns>The grandchild's PID.</returns>
    private static int SpawnGrandchild()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        info.Environment[RoleVariable] = "grandchild";
        using var child = Process.Start(info)!;
        return child.Id;
    }

    /// <summary>Writes UTF-8 text, if any, and flushes.</summary>
    /// <param name="stream">Destination stream.</param>
    /// <param name="text">Text to write; null writes nothing.</param>
    /// <returns>A task that completes when the text is flushed.</returns>
    private static async Task WriteAsync(Stream stream, string? text)
    {
        if (text is null)
        {
            return;
        }

        await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
        await stream.FlushAsync();
    }

    /// <summary>Copies a file's bytes, if named, and flushes.</summary>
    /// <param name="stream">Destination stream.</param>
    /// <param name="baseDirectory">Directory a relative path resolves against.</param>
    /// <param name="path">File to copy; null writes nothing.</param>
    /// <returns>A task that completes when the bytes are flushed.</returns>
    private static async Task WriteFileAsync(Stream stream, string baseDirectory, string? path)
    {
        if (path is null)
        {
            return;
        }

        var bytes = await File.ReadAllBytesAsync(Path.Combine(baseDirectory, path));
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    /// <summary>Writes filler to both streams in alternating chunks.</summary>
    /// <param name="stdout">Standard output.</param>
    /// <param name="stderr">Standard error.</param>
    /// <param name="stdoutBytes">Filler bytes for stdout.</param>
    /// <param name="stderrBytes">Filler bytes for stderr.</param>
    /// <returns>A task that completes when all filler is flushed.</returns>
    private static async Task WriteFillAsync(Stream stdout, Stream stderr, int stdoutBytes, int stderrBytes)
    {
        // Alternate so a reader that drains one stream first deadlocks (jellyfin#17429).
        var chunk = new byte[FillChunkBytes];
        Array.Fill(chunk, (byte)'x');
        chunk[^1] = (byte)'\n';

        while (stdoutBytes > 0 || stderrBytes > 0)
        {
            stdoutBytes -= await WriteChunkAsync(stdout, chunk, stdoutBytes);
            stderrBytes -= await WriteChunkAsync(stderr, chunk, stderrBytes);
        }
    }

    /// <summary>Writes up to one chunk of filler.</summary>
    /// <param name="stream">Destination stream.</param>
    /// <param name="chunk">Filler buffer.</param>
    /// <param name="remaining">Bytes still owed to this stream.</param>
    /// <returns>Bytes written.</returns>
    private static async Task<int> WriteChunkAsync(Stream stream, byte[] chunk, int remaining)
    {
        var count = Math.Min(remaining, chunk.Length);
        if (count > 0)
        {
            await stream.WriteAsync(chunk.AsMemory(0, count));
            await stream.FlushAsync();
        }

        return count;
    }
}
