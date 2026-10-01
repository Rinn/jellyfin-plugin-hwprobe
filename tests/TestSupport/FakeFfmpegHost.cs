using System.Reflection;
using System.Text.Json;

namespace Jellyfin.Plugin.HwProbe.TestSupport;

/// <summary>A temp directory holding FakeFfmpeg scenario files, deleted on dispose.</summary>
internal sealed class FakeFfmpegHost : IDisposable
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Initializes a new instance of the <see cref="FakeFfmpegHost"/> class with a fresh temp directory.</summary>
    public FakeFfmpegHost()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("hwprobe-tests-").FullName;
    }

    /// <summary>Gets the absolute path of the FakeFfmpeg executable.</summary>
    public static string ExecutablePath { get; } = Path.Combine(
        typeof(FakeFfmpegHost).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "FakeFfmpegDirectory").Value!,
        OperatingSystem.IsWindows() ? "FakeFfmpeg.exe" : "FakeFfmpeg");

    /// <summary>Gets the temp directory for scenario, log and PID files.</summary>
    public string Directory { get; }

    /// <summary>Returns a path inside the temp directory.</summary>
    /// <param name="name">File name.</param>
    /// <returns>The absolute path.</returns>
    public string PathFor(string name) => Path.Combine(Directory, name);

    /// <summary>Writes a scenario and returns the environment that selects it.</summary>
    /// <param name="scenario">An object serialized to the FakeFfmpeg scenario format.</param>
    /// <returns>Environment overrides naming the scenario file.</returns>
    public Dictionary<string, string?> Scenario(object scenario)
    {
        var path = PathFor($"scenario-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(scenario, _json));
        return new Dictionary<string, string?> { ["HWPROBE_FAKE_SCENARIO"] = path };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
