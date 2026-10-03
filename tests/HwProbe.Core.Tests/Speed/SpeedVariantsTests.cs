using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Which comparisons <see cref="SpeedVariants"/> offers per backend and test.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedVariantsTests
{
    /// <summary>Low power is measured both ways only on Intel backends, for H.264 and HEVC.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="test">The test key.</param>
    /// <param name="expected">The comparison labels, in order.</param>
    [Theory]
    [InlineData(HwType.none, "pattern|h264-4mbps", "")]
    [InlineData(HwType.qsv, "pattern-1080i|h264-4mbps", "Low power on")]
    [InlineData(HwType.vaapi, "pattern-4k-hdr|hevc-8mbps", "Low power on")]
    [InlineData(HwType.vaapi, "pattern|av1-8mbps", "")]
    [InlineData(HwType.qsv, "pattern-hevc|decode", "")]
    public void LowPowerFollowsTheBackend(HwType type, string test, string expected)
    {
        var spec = SpeedCatalog.Find(test)!;
        var settings = new SpeedSettings();
        var clips = SpeedVariants.Clips([spec], settings).ToDictionary(f => f.FileName, f => "/c/" + f.FileName, StringComparer.Ordinal);
        var cell = SpeedVariants.Base(spec, settings, clips);

        Assert.Equal(expected, string.Join('|', SpeedVariants.For(type, spec, cell, SpeedComparison.LowPower, clips).Select(v => v.Label)));
    }

    /// <summary>The base cell asks upstream for everything a real request carries, with the run's settings.</summary>
    [Fact]
    public void BaseCellIsARealRequest()
    {
        var spec = SpeedCatalog.Find("pattern|h264-4mbps")!;
        var settings = new SpeedSettings { EncoderPreset = "fast", AudioVbr = true, BurnIn = "image" };
        var clips = SpeedVariants.Clips([spec], settings).ToDictionary(f => f.FileName, f => "/c/" + f.FileName, StringComparer.Ordinal);
        var cell = SpeedVariants.Base(spec, settings, clips);

        Assert.True(cell.FullQuality);
        Assert.True(cell.Audio);
        Assert.True(cell.AudioVbr);
        Assert.Equal("fast", cell.EncoderPreset);
        Assert.Equal((1920, 1080, 24f, 4_000_000), (cell.SourceWidth, cell.SourceHeight, cell.SourceFrameRate, cell.VideoBitrate!.Value));
        Assert.Equal((null, null), (cell.MaxWidth, cell.MaxHeight));
        Assert.Equal("/c/speed_1080p_h264.mkv", cell.SourcePath);
        Assert.Equal(("/c/speed_pgs_sub.sup", (string?)null), (cell.GraphicalSubtitlePath, cell.SubtitlePath));
        Assert.DoesNotContain(SpeedCatalog.TextSubtitles, SpeedVariants.Clips([spec], settings));
        Assert.DoesNotContain(SpeedCatalog.ImageSubtitles, SpeedVariants.Clips([SpeedCatalog.Find("pattern|decode")!], settings));
    }

    /// <summary>Every catalog option, at every value it takes, changes the settings.</summary>
    [Fact]
    public void EveryOptionApplies()
    {
        foreach (var option in Jellyfin.Plugin.HwProbe.Core.Data.Catalog.Default.Options)
        {
            var values = option.Switch ? ["true", "false"] : option.Range is [var low, var high] ? [low.ToString(System.Globalization.CultureInfo.InvariantCulture), high.ToString(System.Globalization.CultureInfo.InvariantCulture)] : option.Choices!.Select(c => c.Key).ToArray();
            Assert.All(values, v => Assert.NotNull(SpeedSettingsOptions.Apply(new SpeedSettings(), option.Key, v)));
        }

        Assert.Equal((null, 20, true, "text"), (SpeedSettingsOptions.Apply(new SpeedSettings { EncoderPreset = "fast" }, "EncoderPreset", "auto")!.EncoderPreset, SpeedSettingsOptions.Apply(new SpeedSettings(), "H264Crf", "20")!.H264Crf, SpeedSettingsOptions.Apply(new SpeedSettings(), "DeinterlaceMethod", "bwdif")!.Bwdif, SpeedSettingsOptions.Apply(new SpeedSettings(), "BurnIn", "text")!.BurnIn));
        Assert.Null(SpeedSettingsOptions.Apply(new SpeedSettings(), "Tonemap", "true"));
    }

    /// <summary>Video and output keys are unique, the defaults exist, and a test is keyed video|output.</summary>
    [Fact]
    public void CatalogKeysAreUnique()
    {
        Assert.Equal(SpeedCatalog.Videos.Count, SpeedCatalog.Videos.Select(v => v.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(SpeedCatalog.Outputs.Count, SpeedCatalog.Outputs.Select(o => o.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(SpeedCatalog.DefaultVideos, k => Assert.NotNull(SpeedCatalog.FindVideo(k)));
        Assert.All(SpeedCatalog.DefaultOutputs, k => Assert.NotNull(SpeedCatalog.FindOutput(k)));
        Assert.Null(SpeedCatalog.Find("pattern"));
        Assert.Equal("Test video, H.264 \u2192 HEVC, 720 kbps", SpeedCatalog.Find("pattern|hevc-720kbps")!.Label);
        Assert.Equal((3 * 14) + 1, SpeedCatalog.Outputs.Count);
    }

    /// <summary>Every chosen output runs on every chosen video, video by video; the library video needs a file.</summary>
    [Fact]
    public void OptionsPairVideosWithOutputs()
    {
        var file = new SpeedFile("/m/film.mkv", "Film", TimeSpan.FromMinutes(100), new SpeedFileVideo(0, "hevc", 10, 3840, 2160, 23.976f) { ColorTransfer = "smpte2084" });
        var options = new SpeedOptions(SpeedMethod.Quick, ["pattern", "library", "nope"], ["h264-4mbps", "decode"], SpeedComparison.None, new SpeedSettings());

        Assert.Equal(["pattern|h264-4mbps", "pattern|decode"], options.Resolve().Select(t => t.Key));
        var withFile = (options with { File = file }).Resolve();
        Assert.Equal(["pattern|h264-4mbps", "pattern|decode", "library|h264-4mbps", "library|decode"], withFile.Select(t => t.Key));
        Assert.True(withFile[2].Tonemap);
        Assert.False(withFile[3].Tonemap);
        Assert.Equal(TimeSpan.FromMinutes(10), withFile[2].StartAt);
    }

    /// <summary>Inputs and outputs read plainly.</summary>
    [Fact]
    public void DescriptionsReadPlainly()
    {
        Assert.Equal("1080p H.264, 24 fps, 5.1 AAC", SpeedTestText.Input(SpeedCatalog.FindVideo("pattern")!));
        Assert.Equal("4K HEVC 10-bit HDR, 24 fps, 5.1 AAC", SpeedTestText.Input(SpeedCatalog.FindVideo("pattern-4k-hdr")!));
        Assert.Equal("1080i H.264, 25 fps, 5.1 AAC", SpeedTestText.Input(SpeedCatalog.FindVideo("pattern-1080i")!));
        Assert.Equal("H.264 at 4 Mbps, stereo AAC, tone-mapped to SDR", SpeedTestText.Output(SpeedCatalog.Find("pattern-4k-hdr|h264-4mbps")!));
        Assert.Equal("AV1 at 420 kbps, stereo AAC", SpeedTestText.Output(SpeedCatalog.Find("pattern|av1-420kbps")!));
        Assert.Equal("Decoded only, not encoded", SpeedTestText.Output(SpeedCatalog.Find("anime|decode")!));
        Assert.Equal(("Live-action + CGI", "Tears of Steel", "10.1 s, 2 MB download"), (SpeedCatalog.FindVideo("live-action")!.Name, SpeedCatalog.FindVideo("live-action")!.Title, SpeedCatalog.FindVideo("live-action")!.Origin));
        Assert.Equal("1080p VP9, 24 fps, stereo Opus", SpeedTestText.Input(SpeedCatalog.FindVideo("live-action")!));
        Assert.Equal(("av1", 12, "Professional"), (SpeedCatalog.FindVideo("anime-4k")!.Fixture!.Codec, SpeedCatalog.FindVideo("anime-4k")!.Fixture!.BitDepth, SpeedCatalog.FindVideo("anime-4k")!.Fixture!.Profile));
    }

    /// <summary>A speed report round-trips through JSON with string enums.</summary>
    [Fact]
    public void ReportRoundTrips()
    {
        var report = new SpeedReport(DateTimeOffset.UnixEpoch, new FfmpegSummary("/f", "Server", "8.1.2", true), SpeedMethod.Full, [new SpeedResult(HwType.qsv, "/dev/dri/renderD128", "1080p-h264", string.Empty, 410.5, 9, false, null)]);

        var json = SpeedReportStore.Serialize(report);

        Assert.Contains("\"method\": \"Full\"", json, StringComparison.Ordinal);
        Assert.Equal(json, SpeedReportStore.Serialize(SpeedReportStore.Deserialize(json)!));
        Assert.Null(SpeedReportStore.Deserialize("not json"));
    }
}
