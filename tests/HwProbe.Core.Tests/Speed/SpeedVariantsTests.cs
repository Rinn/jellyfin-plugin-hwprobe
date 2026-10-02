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
    [InlineData(HwType.none, "1080p-h264", "VBR audio on|Preset superfast|Preset faster|CRF 18|CRF 28|1.5 Mbps")]
    [InlineData(HwType.none, "1080p-av1", "VBR audio on|Preset superfast|Preset faster|1.5 Mbps")]
    [InlineData(HwType.qsv, "1080i", "VBR audio on|Preset superfast|Preset faster|1.5 Mbps|Double rate|BWDIF|Low power on|QSV decoders")]
    [InlineData(HwType.vaapi, "2160p-hdr10", "VBR audio on|Preset superfast|Preset faster|6 Mbps|Low power on|VPP tone-mapping on")]
    [InlineData(HwType.nvenc, "decode-hevc", "cuvid decoders")]
    [InlineData(HwType.videotoolbox, "decode-hevc", "")]
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
        var spec = SpeedCatalog.Find("1080p-pgs-subs")!;
        var cell = SpeedVariants.Base(spec, new SpeedSettings { EncoderPreset = "fast", AudioVbr = true }, SpeedVariants.Clips([spec]).ToDictionary(f => f.FileName, f => "/c/" + f.FileName, StringComparer.Ordinal));

        Assert.True(cell.FullQuality);
        Assert.True(cell.Audio);
        Assert.True(cell.AudioVbr);
        Assert.Equal("fast", cell.EncoderPreset);
        Assert.Equal((1920, 1080, 24f, 1280, 720, 4_000_000), (cell.SourceWidth, cell.SourceHeight, cell.SourceFrameRate, cell.MaxWidth!.Value, cell.MaxHeight!.Value, cell.VideoBitrate!.Value));
        Assert.Equal("/c/speed_1080p_h264.mkv", cell.SourcePath);
        Assert.Equal("/c/speed_pgs_sub.sup", cell.GraphicalSubtitlePath);
    }

    /// <summary>Every test key is unique, and the default exists.</summary>
    [Fact]
    public void CatalogKeysAreUnique()
    {
        Assert.Equal(SpeedCatalog.All.Count, SpeedCatalog.All.Select(t => t.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(SpeedCatalog.Default, k => Assert.NotNull(SpeedCatalog.Find(k)));
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
