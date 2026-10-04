using System.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Ffmpeg;

/// <summary>Process-handling behaviour of <see cref="FfmpegRunner"/>, against FakeFfmpeg.</summary>
[Trait("Category", "FakeFfmpeg")]
public sealed class FfmpegRunnerTests : IDisposable
{
    private static readonly TimeSpan _generous = TimeSpan.FromSeconds(30);

    private readonly FakeFfmpegHost _host = new();
    private readonly FfmpegRunner _runner = new();

    /// <summary>ffmpeg runs in the C locale whatever the host's language, unless the invocation sets its own.</summary>
    [Fact]
    public void RunsInTheCLocale()
    {
        var plain = FfmpegRunner.CreateStartInfo(new FfmpegInvocation("ffmpeg", "-version", new Dictionary<string, string?>(), _generous));
        var overridden = FfmpegRunner.CreateStartInfo(new FfmpegInvocation("ffmpeg", "-version", new Dictionary<string, string?> { ["LC_ALL"] = "en_US.UTF-8" }, _generous));

        Assert.Equal("C", plain.Environment["LC_ALL"]);
        Assert.Equal("en_US.UTF-8", overridden.Environment["LC_ALL"]);
    }

    /// <summary>Exit code, stderr and the final progress frame are all captured.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CapturesExitCodeStderrAndFrames()
    {
        var env = _host.Scenario(new { Default = new { Stderr = "decoder said hi\n", Frames = 10, ExitCode = 3 } });

        var result = await RunAsync(env, _generous);

        Assert.Equal(FfmpegRunStatus.Exited, result.Status);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("decoder said hi\n", result.Stderr);
        Assert.Equal(10, result.Frames);
        Assert.Null(result.LaunchError);
    }

    /// <summary>An immediate silent exit is a clean Exited result with no frames.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImmediateSilentExit()
    {
        var env = _host.Scenario(new { });

        var result = await RunAsync(env, _generous);

        Assert.Equal(FfmpegRunStatus.Exited, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        Assert.Null(result.Frames);
    }

    /// <summary>A missing binary surfaces as LaunchFailed, distinct from any exit code.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MissingBinaryIsLaunchFailed()
    {
        var invocation = new FfmpegInvocation(_host.PathFor("no-such-ffmpeg"), string.Empty, new Dictionary<string, string?>(), _generous);

        var result = await _runner.RunAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(FfmpegRunStatus.LaunchFailed, result.Status);
        Assert.Null(result.ExitCode);
        Assert.False(string.IsNullOrEmpty(result.LaunchError));
    }

    /// <summary>More than the pipe buffer on both streams completes instead of deadlocking (jellyfin#17429).</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LargeInterleavedOutputDoesNotDeadlock()
    {
        const int Bytes = 2 * 1024 * 1024;
        var env = _host.Scenario(new { Default = new { StdoutFillBytes = Bytes, StderrFillBytes = Bytes } });

        // A sequential-read bug hangs before the runner's timeout starts, so bound the call here.
        var result = await RunAsync(env, _generous).WaitAsync(_generous, TestContext.Current.CancellationToken);

        Assert.Equal(FfmpegRunStatus.Exited, result.Status);
        Assert.Equal(Bytes, result.Stdout.Length);
        Assert.Equal(Bytes, result.Stderr.Length);
    }

    /// <summary>Overrides reach the child: set values appear, null values are removed.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task EnvironmentOverridesReachChild()
    {
        var env = _host.Scenario(new { Default = new { EchoEnv = (string[])["LIBVA_DRIVER_NAME", "HOME"] } });
        env["LIBVA_DRIVER_NAME"] = "i965";
        env["HOME"] = null;

        var result = await RunAsync(env, _generous);

        Assert.Equal("LIBVA_DRIVER_NAME=i965\nHOME!unset\n", result.Stdout);
    }

    /// <summary>On timeout the child and its grandchild are both dead when the call returns.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TimeoutKillsEntireTree()
    {
        var pidFile = _host.PathFor("pids.txt");
        var env = _host.Scenario(new { Default = new { SpawnChild = true, PidFile = pidFile, Hang = true } });

        // Long enough for a cold start to write both PIDs before the runner's timeout kills the tree.
        var result = await RunAsync(env, TimeSpan.FromSeconds(10));

        Assert.Equal(FfmpegRunStatus.TimedOut, result.Status);
        Assert.Null(result.ExitCode);
        await AssertAllDeadAsync(pidFile);
    }

    /// <summary>Caller cancellation throws promptly, after the tree is already reaped.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CancellationReapsTreeThenThrows()
    {
        var pidFile = _host.PathFor("pids.txt");
        var env = _host.Scenario(new { Default = new { SpawnChild = true, PidFile = pidFile, Hang = true } });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var invocation = new FfmpegInvocation(FakeFfmpegHost.ExecutablePath, "-i x", env, _generous);
        var run = _runner.RunAsync(invocation, cts.Token);

        // Cancel only once both processes exist; a fixed delay raced a slow cold start.
        await WaitForPidsAsync(pidFile);
        var stopwatch = Stopwatch.StartNew();
        await cts.CancelAsync();
        OperationCanceledException? canceled = null;
        try
        {
            await run;
        }
        catch (OperationCanceledException ex)
        {
            canceled = ex;
        }

        Assert.NotNull(canceled);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"cancellation took {stopwatch.Elapsed}");
        await AssertAllDeadAsync(pidFile);
    }

    /// <inheritdoc/>
    public void Dispose() => _host.Dispose();

    /// <summary>Waits until FakeFfmpeg has written both PIDs.</summary>
    /// <param name="pidFile">File of PIDs written by FakeFfmpeg.</param>
    /// <returns>A task that completes when the file holds two PIDs.</returns>
    private static async Task WaitForPidsAsync(string pidFile)
    {
        var deadline = DateTime.UtcNow + _generous;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(pidFile, TestContext.Current.CancellationToken);
                if (lines.Length == 2 && lines.All(l => int.TryParse(l, out _)))
                {
                    return;
                }
            }
            catch (IOException)
            {
                // Not written yet, or still open for writing (a sharing violation on Windows).
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"FakeFfmpeg never wrote two PIDs to {pidFile}");
    }

    /// <summary>Waits briefly for every PID in the file to disappear, failing if any survives.</summary>
    /// <param name="pidFile">File of PIDs written by FakeFfmpeg.</param>
    /// <returns>A task representing the assertion.</returns>
    private static async Task AssertAllDeadAsync(string pidFile)
    {
        var pids = (await File.ReadAllLinesAsync(pidFile, TestContext.Current.CancellationToken)).Select(int.Parse).ToList();
        Assert.Equal(2, pids.Count);

        // SIGKILL is async; give the processes a moment to exit.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (pids.Any(IsAlive) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.All(pids, pid => Assert.False(IsAlive(pid), $"PID {pid} survived"));
    }

    /// <summary>Reports whether a process with this PID is still running.</summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>True if the process exists and has not exited.</returns>
    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Runs FakeFfmpeg with the given environment and timeout.</summary>
    /// <param name="env">Environment overrides, including the scenario selector.</param>
    /// <param name="timeout">The hard timeout.</param>
    /// <returns>The run result.</returns>
    private Task<FfmpegRunResult> RunAsync(Dictionary<string, string?> env, TimeSpan timeout) =>
        _runner.RunAsync(new FfmpegInvocation(FakeFfmpegHost.ExecutablePath, "-i x", env, timeout), TestContext.Current.CancellationToken);
}
