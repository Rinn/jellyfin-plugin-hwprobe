using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Turning catalog suites into runs in <see cref="SpeedSuites"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedSuitesTests
{
    /// <summary>Thread limits double up to the CPU count, include it, and stop at the most the setting offers.</summary>
    /// <param name="cpus">The server's logical CPU count.</param>
    /// <param name="limits">The thread limits after Auto.</param>
    [Theory]
    [InlineData(6, "1,2,4,6")]
    [InlineData(4, "1,2,4")]
    [InlineData(32, "1,2,4,8,16")]
    public void ThreadStepsFollowTheCpu(int cpus, string limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var suite = Suite("threads");
        var auto = Option("EncodingThreadCount").Choices![0];
        var expected = limits.Split(',').Select(n => (n == "1" ? suite.ThreadLabelOne : suite.ThreadLabel).Replace("{n}", n, StringComparison.Ordinal)).Prepend(auto.Label);

        var steps = SpeedSuites.Steps(suite, cpus);

        Assert.Equal(expected, steps.Select(s => s.Label));
        Assert.Equal(auto.Key, steps[0].Options["EncodingThreadCount"]);
        Assert.Equal(limits.Split(','), steps.Skip(1).Select(s => s.Options["EncodingThreadCount"]));
    }

    /// <summary>The server's value is added as a last step when a one-setting suite lacks it, so suggestions have it to compare against.</summary>
    [Fact]
    public void AddsTheServersValue()
    {
        var presets = SpeedSuites.Steps(Suite("presets"), 8, new SpeedSettings { EncoderPreset = "veryfast" });
        var threads = SpeedSuites.Steps(Suite("threads"), 8, new SpeedSettings { EncodingThreadCount = 6 });

        var veryfast = Option("EncoderPreset").Choices!.Single(c => c.Key == "veryfast").Label;

        Assert.Equal(($"{veryfast} ({Catalog.Default.Labels["ServerSettingAfter"]})", "veryfast"), (presets[^1].Label, presets[^1].Options["EncoderPreset"]));
        Assert.Equal(Suite("presets").Steps.Count + 1, presets.Count);
        Assert.Equal("6", threads[^1].Options["EncodingThreadCount"]);
        Assert.Equal(Suite("presets").Steps.Count, SpeedSuites.Steps(Suite("presets"), 8, new SpeedSettings { EncoderPreset = "medium" }).Count);
        Assert.Equal(Suite("lowpower").Steps.Count, SpeedSuites.Steps(Suite("lowpower"), 8, new SpeedSettings()).Count);
    }

    /// <summary>Every suite but VBR audio copies the audio, so only video is measured, including the server's added step; VBR transcodes it.</summary>
    [Fact]
    public void SuitesCopyAudioExceptVbr()
    {
        var steps = Catalog.Default.Suites.Where(s => s.Key != "decode").ToDictionary(s => s.Key, s => SpeedSuites.Steps(s, 8, new SpeedSettings { EncoderPreset = "veryfast" }));

        Assert.All(steps.Where(s => s.Key != "audio").SelectMany(s => s.Value), step => Assert.Equal("copy", step.Options["Audio"]));
        Assert.All(steps["audio"], step => Assert.Equal("transcode", step.Options["Audio"]));
        Assert.Equal(("veryfast", "copy"), (steps["presets"][^1].Options["EncoderPreset"], steps["presets"][^1].Options["Audio"]));
    }

    /// <summary>Steps for another backend are left out: tone mapping offers VPP on Intel and VideoToolbox's on Apple.</summary>
    [Fact]
    public void StepsFollowTheBackend()
    {
        var tonemap = Suite("tonemap");
        var (general, vpp, videoToolbox, off) = (tonemap.Steps[0].Label, tonemap.Steps[1].Label, tonemap.Steps[2].Label, tonemap.Steps[3].Label);

        Assert.Equal([general, vpp, off], SpeedSuites.Steps(tonemap, 8, hardware: HwType.qsv).Select(s => s.Label));
        Assert.Equal([general, videoToolbox, off], SpeedSuites.Steps(tonemap, 8, hardware: HwType.videotoolbox).Select(s => s.Label));
        Assert.Equal([general, off], SpeedSuites.Steps(tonemap, 8, hardware: HwType.nvenc).Select(s => s.Label));
        Assert.Equal(tonemap.Steps.Select(s => s.Label), SpeedSuites.Steps(tonemap, 8).Select(s => s.Label));
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

    /// <summary>Returns a catalog option.</summary>
    /// <param name="key">Its key.</param>
    /// <returns>The option.</returns>
    private static CatalogSetting Option(string key) => Catalog.Default.Options.Single(o => o.Key == key);

    /// <summary>Returns a report with a working QSV backend whose low-power H.264 test had an outcome.</summary>
    /// <param name="lowPower">The low-power test's outcome.</param>
    /// <returns>The report.</returns>
    private static CapabilityReport Report(ProbeOutcome lowPower) =>
        Reports.With([Reports.Backend(HwType.qsv, "/dev/dri/renderD128", tier: PipelineTier.FullOpencl, encode: new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["h264_lowpower"] = lowPower })]);
}
