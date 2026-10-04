using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Turning catalog suites into runs in <see cref="SpeedSuites"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedSuitesTests
{
    /// <summary>Thread limits double up to the CPU count, include it, and stop at the most the setting offers.</summary>
    /// <param name="cpus">The server's logical CPU count.</param>
    /// <param name="expected">The step labels.</param>
    [Theory]
    [InlineData(6, "Auto,1 thread,2 threads,4 threads,6 threads")]
    [InlineData(4, "Auto,1 thread,2 threads,4 threads")]
    [InlineData(32, "Auto,1 thread,2 threads,4 threads,8 threads,16 threads")]
    public void ThreadStepsFollowTheCpu(int cpus, string expected)
    {
        var steps = SpeedSuites.Steps(Suite("threads"), cpus);

        Assert.Equal(expected, string.Join(',', steps.Select(s => s.Label)));
        Assert.Equal("-1", steps[0].Options["EncodingThreadCount"]);
    }

    /// <summary>The server's value is added as a last step when a one-setting suite lacks it, so suggestions have it to compare against.</summary>
    [Fact]
    public void AddsTheServersValue()
    {
        var presets = SpeedSuites.Steps(Suite("presets"), 8, new SpeedSettings { EncoderPreset = "veryfast" });
        var threads = SpeedSuites.Steps(Suite("threads"), 8, new SpeedSettings { EncodingThreadCount = 6 });

        Assert.Equal(("veryfast (server setting)", "veryfast"), (presets[^1].Label, presets[^1].Options["EncoderPreset"]));
        Assert.Equal("6", threads[^1].Options["EncodingThreadCount"]);
        Assert.Equal(5, SpeedSuites.Steps(Suite("presets"), 8, new SpeedSettings { EncoderPreset = "medium" }).Count);
        Assert.Equal(2, SpeedSuites.Steps(Suite("lowpower"), 8, new SpeedSettings()).Count);
    }

    /// <summary>Steps for another backend are left out: tone mapping offers VPP on Intel and VideoToolbox's on Apple.</summary>
    [Fact]
    public void StepsFollowTheBackend()
    {
        Assert.Equal(["Tone mapping", "VPP tone mapping", "Tone mapping off"], SpeedSuites.Steps(Suite("tonemap"), 8, hardware: HwType.qsv).Select(s => s.Label));
        Assert.Equal(["Tone mapping", "VideoToolbox tone mapping", "Tone mapping off"], SpeedSuites.Steps(Suite("tonemap"), 8, hardware: HwType.videotoolbox).Select(s => s.Label));
        Assert.Equal(["Tone mapping", "Tone mapping off"], SpeedSuites.Steps(Suite("tonemap"), 8, hardware: HwType.nvenc).Select(s => s.Label));
    }

    /// <summary>A step's own videos replace the suite's; the rest keep the suite's.</summary>
    [Fact]
    public void StepsCanNameTheirOwnVideos()
    {
        var suite = new CatalogSuite
        {
            Key = "mixed",
            Name = "Mixed",
            Description = "Two inputs.",
            Videos = ["drama"],
            Outputs = ["h264-8mbps"],
            Steps = [new CatalogSuiteStep { Label = "Film" }, new CatalogSuiteStep { Label = "Interlaced", Videos = ["pattern-1080i"] }],
        };

        var steps = SpeedSuites.Steps(suite, 8);

        Assert.Equal(["drama"], steps[0].Videos);
        Assert.Equal(["pattern-1080i"], steps[1].Videos);
    }

    /// <summary>Suites run on the configured backend and software, or one of them, as each says.</summary>
    [Fact]
    public void BackendsFollowTheSuite()
    {
        Assert.Equal([HwType.qsv, HwType.none], SpeedSuites.Backends(Suite("presets"), HwType.qsv));
        Assert.Equal([HwType.none], SpeedSuites.Backends(Suite("presets"), HwType.none));
        Assert.Equal([HwType.none], SpeedSuites.Backends(Suite("threads"), HwType.nvenc));
        Assert.Empty(SpeedSuites.Backends(Suite("lowpower"), HwType.none));
    }

    /// <summary>The low-power suite is offered only on QSV with a low-power encoder that passed its probe.</summary>
    [Fact]
    public void LowPowerNeedsAWorkingEncoder()
    {
        Assert.True(SpeedSuites.Offered(Suite("lowpower"), Report(ProbeOutcome.Pass), HwType.qsv));
        Assert.False(SpeedSuites.Offered(Suite("lowpower"), Report(ProbeOutcome.CodecUnsupported), HwType.qsv));
        Assert.False(SpeedSuites.Offered(Suite("lowpower"), Report(ProbeOutcome.Pass), HwType.vaapi));
        Assert.True(SpeedSuites.Offered(Suite("presets"), Report(ProbeOutcome.CodecUnsupported), HwType.qsv));
    }

    /// <summary>Returns a catalog suite.</summary>
    /// <param name="key">Its key.</param>
    /// <returns>The suite.</returns>
    private static CatalogSuite Suite(string key) => Catalog.Default.Suites.Single(s => s.Key == key);

    /// <summary>Returns a report with a working QSV backend whose low-power H.264 test had an outcome.</summary>
    /// <param name="lowPower">The low-power test's outcome.</param>
    /// <returns>The report.</returns>
    private static CapabilityReport Report(ProbeOutcome lowPower)
    {
        var empty = new Dictionary<string, ProbeOutcome>();
        var encode = new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["h264_lowpower"] = lowPower };
        var qsv = new BackendReport(HwType.qsv, "/dev/dri/renderD128", BackendVerdict.Viable, PipelineTier.FullOpencl, empty, encode, empty, empty, empty, string.Empty);
        return new CapabilityReport(CapabilityReport.CurrentSchemaVersion, DateTimeOffset.UnixEpoch, "f", new FfmpegSummary("/ffmpeg", "CommandLine", "8.1.2", true), new HostSummary("linux", "6.8", null), new StageASummary([], new Dictionary<HwType, BuildStatus>(), new Dictionary<string, bool>()), [qsv], [], []);
    }
}
