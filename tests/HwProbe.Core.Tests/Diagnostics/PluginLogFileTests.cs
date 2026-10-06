using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Diagnostics;

/// <summary>Reading Jellyfin's log files from disk in <see cref="PluginLog"/>.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class PluginLogFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-log-").FullName;

    /// <summary>Every log file is read oldest first, including one Jellyfin holds open for writing.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReadsEveryFileOldestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_root, "log_20261004.log"), "[2026-10-04 10:00:00.000 +00:00] [INF] [1] Jellyfin.Plugin.HwProbe.Probing.ProbeService: first\n", ct);
        await using var open = new FileStream(Path.Combine(_root, "log_20261005.log"), FileMode.Create, FileAccess.Write, FileShare.Read);
        await open.WriteAsync("[2026-10-05 10:00:00.000 +00:00] [INF] [1] Jellyfin.Plugin.HwProbe.Probing.ProbeService: second\n"u8.ToArray(), ct);
        await open.FlushAsync(ct);

        var text = await PluginLog.ReadAsync(_root, ct);

        Assert.True(text.IndexOf("first", StringComparison.Ordinal) < text.IndexOf("second", StringComparison.Ordinal));
        Assert.Empty(await PluginLog.ReadAsync(Path.Combine(_root, "none"), ct));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
