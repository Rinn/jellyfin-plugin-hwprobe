using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Which comparisons <see cref="SpeedVariants"/> offers per backend and test.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedVariantsTests
{
    private const SpeedComparison All = SpeedComparison.AudioVbr | SpeedComparison.Preset | SpeedComparison.Quality | SpeedComparison.Bitrate | SpeedComparison.Deinterlace | SpeedComparison.Paths;

    /// <summary>Labels per backend and test with every comparison asked for.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="test">The test key.</param>
    /// <param name="expected">The comparison labels, in order.</param>
    [Theory]
    [InlineData(HwType.none, "pattern|720p-h264", "VBR audio on|Preset superfast|Preset faster|CRF 18|CRF 28|1.5 Mbps")]
    [InlineData(HwType.none, "pattern|720p-av1", "VBR audio on|Preset superfast|Preset faster|1.5 Mbps")]
    [InlineData(HwType.qsv, "pattern-1080i|720p-h264", "VBR audio on|Preset superfast|Preset faster|1.5 Mbps|Double rate|BWDIF|Low power on|QSV decoders")]
    [InlineData(HwType.vaapi, "pattern-4k-hdr|1080p-h264", "VBR audio on|Preset superfast|Preset faster|6 Mbps|Low power on|VPP tone-mapping on")]
    [InlineData(HwType.nvenc, "pattern-hevc|decode", "cuvid decoders")]
    [InlineData(HwType.videotoolbox, "pattern-hevc|decode", "")]
    public void ComparisonsFollowTheBackend(HwType type, string test, string expected)
    {
        var spec = SpeedCatalog.Find(test)!;
        var cell = SpeedVariants.Base(spec, new SpeedSettings(), SpeedVariants.Clips([spec]).ToDictionary(f => f.FileName, f => "/c/" + f.FileName, StringComparer.Ordinal));

        Assert.Equal(expected, string.Join('|', SpeedVariants.For(type, spec, cell, All).Select(v => v.Label)));
    }

    /// <summary>The base cell asks upstream for everything a real request carries.</summary>
    [Fact]
    public void BaseCellIsARealRequest()
    {
        var spec = SpeedCatalog.Find("pattern|720p-h264-pgs")!;
        var cell = SpeedVariants.Base(spec, new SpeedSettings { EncoderPreset = "fast", AudioVbr = true }, SpeedVariants.Clips([spec]).ToDictionary(f => f.FileName, f => "/c/" + f.FileName, StringComparer.Ordinal));

        Assert.True(cell.FullQuality);
        Assert.True(cell.Audio);
        Assert.True(cell.AudioVbr);
        Assert.Equal("fast", cell.EncoderPreset);
        Assert.Equal((1920, 1080, 24f, 1280, 720, 4_000_000), (cell.SourceWidth, cell.SourceHeight, cell.SourceFrameRate, cell.MaxWidth!.Value, cell.MaxHeight!.Value, cell.VideoBitrate!.Value));
        Assert.Equal("/c/speed_1080p_h264.mkv", cell.SourcePath);
        Assert.Equal("/c/speed_pgs_sub.sup", cell.GraphicalSubtitlePath);
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
        Assert.Equal("Test video \u2192 720p H.264, 4 Mbps", SpeedCatalog.Find("pattern|720p-h264")!.Label);
    }

    /// <summary>Every chosen output runs on every chosen video, video by video; the library video needs a file.</summary>
    [Fact]
    public void OptionsPairVideosWithOutputs()
    {
        var file = new SpeedFile("/m/film.mkv", "Film", TimeSpan.FromMinutes(100), new SpeedFileVideo(0, "hevc", 10, 3840, 2160, 23.976f) { ColorTransfer = "smpte2084" });
        var options = new SpeedOptions(SpeedMethod.Quick, ["pattern", "library", "nope"], ["720p-h264", "decode"], SpeedComparison.None, new SpeedSettings());

        Assert.Equal(["pattern|720p-h264", "pattern|decode"], options.Resolve().Select(t => t.Key));
        var withFile = (options with { File = file }).Resolve();
        Assert.Equal(["pattern|720p-h264", "pattern|decode", "library|720p-h264", "library|decode"], withFile.Select(t => t.Key));
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
        Assert.Equal("720p H.264 at 4 Mbps, stereo AAC, tone-mapped to SDR, PGS subtitles burned in", SpeedTestText.Output(SpeedCatalog.Find("pattern-4k-hdr|720p-h264-pgs")!));
        Assert.Equal("Decoded only, not encoded", SpeedTestText.Output(SpeedCatalog.Find("anime|decode")!));
        Assert.Equal(("Live action", "Tears of Steel", "10 MB download"), (SpeedCatalog.FindVideo("live-action")!.Name, SpeedCatalog.FindVideo("live-action")!.Title, SpeedCatalog.FindVideo("live-action")!.Origin));
        Assert.StartsWith("1080p H.264", SpeedTestText.Input(SpeedCatalog.FindVideo("live-action")!), StringComparison.Ordinal);
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
