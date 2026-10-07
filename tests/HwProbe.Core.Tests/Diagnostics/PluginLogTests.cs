using System.IO.Compression;
using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Diagnostics;

/// <summary>Picking HwProbe's entries out of Jellyfin's log in <see cref="PluginLog"/>.</summary>
[Trait("Category", "Unit")]
public sealed class PluginLogTests
{
    /// <summary>HwProbe's entries and those naming it are kept with their stack traces; other entries and theirs are dropped.</summary>
    [Fact]
    public void KeepsHwProbeEntriesWithTheirContinuations()
    {
        string[] lines =
        [
            "[2026-10-04 21:35:43.291 +00:00] [INF] [4] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: \"HwProbe\" \"0.18.0.0\"",
            "[2026-10-04 21:35:44.000 +00:00] [INF] [4] Emby.Server.Implementations.ApplicationHost: Core startup complete",
            "[2026-10-04 21:36:00.000 +00:00] [ERR] [9] Jellyfin.Plugin.HwProbe.Probing.ProbeService: HwProbe failed.",
            "System.NullReferenceException: Object reference not set to an instance of an object.",
            "   at Jellyfin.Plugin.HwProbe.Jellyfin.ArgumentSource.Build()",
            "[2026-10-04 21:36:01.000 +00:00] [ERR] [9] MediaBrowser.Controller: Other failure",
            "   at Somewhere.Else()",
        ];

        Assert.Equal([lines[0], lines[2], lines[3], lines[4]], PluginLog.Filter(lines));
    }

    /// <summary>A file added to a bundle sits beside what was there, replacing one of the same name.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AddsAFileToABundle()
    {
        var ct = TestContext.Current.CancellationToken;
        using var original = new MemoryStream();
        using (var zip = new ZipArchive(original, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var entry = await zip.CreateEntry("report.json").OpenAsync(ct);
            await entry.WriteAsync("{}"u8.ToArray(), ct);
        }

        var once = await DiagnosticsBundle.WithFileAsync(original.ToArray(), "jellyfin.log", "old", ct);
        var twice = await DiagnosticsBundle.WithFileAsync(once, "jellyfin.log", "new", ct);

        using var read = new ZipArchive(new MemoryStream(twice), ZipArchiveMode.Read);
        Assert.Equal(["jellyfin.log", "report.json"], read.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        var logEntry = read.GetEntry("jellyfin.log");
        Assert.NotNull(logEntry);
        using var log = new StreamReader(await logEntry.OpenAsync(ct));
        Assert.Equal("new", await log.ReadToEndAsync(ct));
    }
}
