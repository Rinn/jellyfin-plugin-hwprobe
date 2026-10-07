using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Which comparisons <see cref="SpeedVariants"/> offers per backend and test.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedVariantsTests
{
    /// <summary>The run's low-power choice reaches QSV alone, for H.264 and HEVC; VAAPI keeps the server's.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="test">The test key.</param>
    /// <param name="expected">Whether the cell asks for low power.</param>
    [Theory]
    [InlineData(HwType.qsv, "pattern|h264-4mbps", true)]
    [InlineData(HwType.qsv, "pattern|hevc-8mbps", false)]
    [InlineData(HwType.vaapi, "pattern|h264-4mbps", false)]
    [InlineData(HwType.qsv, "pattern|av1-8mbps", false)]
    public void LowPowerReachesQsvAlone(HwType type, string test, bool expected)
    {
        var spec = Test(test);
        var settings = new SpeedSettings { QsvLowPowerH264 = true };
        var clips = SpeedVariants.Clips([spec], settings).ToDictionary(f => f.FileName, f => "/c/" + f.FileName, StringComparer.Ordinal);

        Assert.Equal(expected, SpeedVariants.ForBackend(type, spec, SpeedVariants.Base(spec, settings, clips), settings).LowPower);
    }

    /// <summary>The base cell asks upstream for everything a real request carries, with the run's settings.</summary>
    [Fact]
    public void BaseCellIsARealRequest()
    {
        var spec = Test("pattern|h264-4mbps");
        var settings = new SpeedSettings { EncoderPreset = "fast", AudioVbr = true, BurnIn = "image" };
        var clips = SpeedVariants.Clips([spec], settings).ToDictionary(f => f.FileName, f => "/c/" + f.FileName, StringComparer.Ordinal);
        var cell = SpeedVariants.Base(spec, settings, clips);

        Assert.True(cell.FullQuality);
        Assert.True(cell.Audio);
        Assert.True(cell.AudioVbr);
        Assert.Equal("fast", cell.EncoderPreset);
        Assert.Equal((spec.Width, spec.Height, spec.FrameRate, spec.Bitrate), (cell.SourceWidth, cell.SourceHeight, cell.SourceFrameRate, Assert.NotNull(cell.VideoBitrate)));
        Assert.Equal((null, null), (cell.MaxWidth, cell.MaxHeight));
        Assert.NotNull(spec.Fixture);
        Assert.Equal("/c/" + spec.Fixture.FileName, cell.SourcePath);
        Assert.Equal(("/c/" + SpeedCatalog.ImageSubtitles.FileName, (string?)null), (cell.GraphicalSubtitlePath, cell.SubtitlePath));
        Assert.DoesNotContain(SpeedCatalog.TextSubtitles, SpeedVariants.Clips([spec], settings));
        Assert.DoesNotContain(SpeedCatalog.ImageSubtitles, SpeedVariants.Clips([Test("pattern|decode")], settings));
    }

    /// <summary>An image cell carries the trickplay settings and none of a transcode's bitrate, audio, or subtitles.</summary>
    [Fact]
    public void ImageCellCarriesTheTrickplaySettings()
    {
        var spec = Test("pattern|trickplay");
        var settings = new SpeedSettings { BurnIn = "text", TrickplayKeyFrames = true, TrickplayHwEncoding = true, TrickplayThreads = 4, TrickplayQscale = 8, TrickplayWidth = 640, TrickplayInterval = 5000 };
        var clips = SpeedVariants.Clips([spec], settings).ToDictionary(f => f.FileName, f => "/c/" + f.FileName, StringComparer.Ordinal);
        var cell = SpeedVariants.Base(spec, settings, clips);

        Assert.True(spec.Images);
        Assert.Equal(new ImageJob(640, 5000, 8, 4, HwEncoding: true, KeyFramesOnly: true), cell.Images);
        Assert.Equal(("mjpeg", (int?)null, false, (string?)null), (cell.OutputCodec, cell.VideoBitrate, cell.Audio, cell.SubtitlePath));
        Assert.DoesNotContain(SpeedCatalog.TextSubtitles, SpeedVariants.Clips([spec], settings));
        Assert.Equal("MJPEG images at an interval", SpeedTestText.Output(spec));
        Assert.Null(SpeedVariants.Base(Test("pattern|h264-8mbps"), settings, clips).Images);
    }

    /// <summary>A sample without audio asks upstream for no audio stream, so ffmpeg isn't told to map one that isn't there.</summary>
    [Fact]
    public void SilentSampleHasNoAudio()
    {
        var spec = Test("drama-8k|h264-8mbps");
        var clips = SpeedVariants.Clips([spec], new SpeedSettings()).ToDictionary(f => f.FileName, f => "/c/" + f.FileName, StringComparer.Ordinal);

        Assert.False(SpeedVariants.Base(spec, new SpeedSettings(), clips).Audio);
    }

    /// <summary>Every catalog option, at every value it takes, changes the settings.</summary>
    [Fact]
    public void EveryOptionApplies()
    {
        foreach (var option in Catalog.Default.Options)
        {
            var values = option.Switch ? ["true", "false"] : option.Range is [var low, var high] ? [low.ToString(System.Globalization.CultureInfo.InvariantCulture), high.ToString(System.Globalization.CultureInfo.InvariantCulture)] : option.Choices?.Select(c => c.Key).ToArray();
            Assert.NotNull(values);
            Assert.All(values, v => Assert.NotNull(SpeedSettingsOptions.Apply(new SpeedSettings(), option.Key, v)));
        }

        var preset = SpeedSettingsOptions.Apply(new SpeedSettings { EncoderPreset = "fast" }, "EncoderPreset", "auto");
        var crf = SpeedSettingsOptions.Apply(new SpeedSettings(), "H264Crf", "20");
        var bwdif = SpeedSettingsOptions.Apply(new SpeedSettings(), "DeinterlaceMethod", "bwdif");
        var burnIn = SpeedSettingsOptions.Apply(new SpeedSettings(), "BurnIn", "text");
        Assert.NotNull(preset);
        Assert.NotNull(crf);
        Assert.NotNull(bwdif);
        Assert.NotNull(burnIn);
        Assert.Equal((null, 20, true, "text"), (preset.EncoderPreset, crf.H264Crf, bwdif.Bwdif, burnIn.BurnIn));
        Assert.Null(SpeedSettingsOptions.Apply(new SpeedSettings(), "Nonsense", "true"));
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
        Assert.Equal("Test video, H.264 \u2192 HEVC, 720 kbps", Test("pattern|hevc-720kbps").Label);
        Assert.Equal((Catalog.Default.Codecs.Count * Catalog.Default.Qualities.Count) + 1 + Catalog.Default.Images.Count, SpeedCatalog.Outputs.Count);
    }

    /// <summary>Every chosen output runs on every chosen video, video by video; the library video needs a file.</summary>
    [Fact]
    public void OptionsPairVideosWithOutputs()
    {
        var file = new SpeedFile("/m/film.mkv", "Film", TimeSpan.FromMinutes(100), new SpeedFileVideo(0, "hevc", 10, 3840, 2160, 23.976f) { ColorTransfer = "smpte2084" });
        var options = new SpeedOptions(SpeedMethod.Quick, ["pattern", "library", "nope"], ["h264-4mbps", "decode"], new SpeedSettings());

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
        Assert.Equal("1080p H.264, 24 fps, 5.1 AAC", SpeedTestText.Input(Video("pattern")));
        Assert.Equal("4K HEVC 10-bit HDR, 24 fps, 5.1 AAC", SpeedTestText.Input(Video("pattern-4k-hdr")));
        Assert.Equal("1080i H.264, 25 fps, 5.1 AAC", SpeedTestText.Input(Video("pattern-1080i")));
        Assert.Equal("H.264 at 4 Mbps, stereo AAC, tone-mapped to SDR", SpeedTestText.Output(Test("pattern-4k-hdr|h264-4mbps")));
        Assert.Equal("AV1 at 420 kbps, stereo AAC", SpeedTestText.Output(Test("pattern|av1-420kbps")));
        Assert.Equal("Decoded only, not encoded", SpeedTestText.Output(Test("anime|decode")));
        Assert.Equal("1080p VP9, 24 fps, stereo Opus", SpeedTestText.Input(Video("live-action")));
        Assert.Equal("8K VP9, 25 fps", SpeedTestText.Input(Video("drama-8k")));
        Assert.Equal("H.264 at 8 Mbps", SpeedTestText.Output(Test("drama-8k|h264-8mbps")));
    }

    /// <summary>A speed report round-trips through compact JSON with string enums, nulls left out and text unescaped; indented JSON saved before still reads.</summary>
    [Fact]
    public void ReportRoundTrips()
    {
        var report = new SpeedReport(DateTimeOffset.UnixEpoch, new FfmpegSummary("/f", "Server", "8.1.2", true), SpeedMethod.Full, [new SpeedResult(HwType.qsv, "/dev/dri/renderD128", "1080p-h264", string.Empty, 410.5, 9, false, null) { Label = "Live action \u2192 H.264" }]);

        var json = SpeedReportStore.Serialize(report);

        Assert.Contains("\"method\":\"Full\"", json, StringComparison.Ordinal);
        Assert.Contains("Live action \u2192 H.264", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"note\"", json, StringComparison.Ordinal);
        Assert.Equal(410.5, SpeedReportStore.Deserialize("{\n  \"method\": \"Full\",\n  \"results\": [ { \"type\": \"qsv\", \"test\": \"a\", \"fps\": 410.5, \"note\": null } ]\n}")?.Results[0].Fps);
        var roundTripped = SpeedReportStore.Deserialize(json);
        Assert.NotNull(roundTripped);
        Assert.Equal(json, SpeedReportStore.Serialize(roundTripped));
        Assert.Null(SpeedReportStore.Deserialize("not json"));
    }

    /// <summary>Returns a test from the catalog, failing the test when there's none.</summary>
    /// <param name="key">The test's key, video|output.</param>
    /// <returns>The test.</returns>
    private static SpeedTest Test(string key)
    {
        var test = SpeedCatalog.Find(key);
        Assert.NotNull(test);
        return test;
    }

    /// <summary>Returns a video from the catalog, failing the test when there's none.</summary>
    /// <param name="key">The video's key.</param>
    /// <returns>The video.</returns>
    private static SpeedVideo Video(string key)
    {
        var video = SpeedCatalog.FindVideo(key);
        Assert.NotNull(video);
        return video;
    }
}
