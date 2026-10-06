using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Report;

/// <summary>JSON shape of <see cref="ReportStore"/>.</summary>
[Trait("Category", "Unit")]
public sealed class ReportStoreTests
{
    /// <summary>A report round-trips and carries schemaVersion, string enums and camelCase.</summary>
    [Fact]
    public void RoundTripsWithDocumentedShape()
    {
        var report = Sample("sha256:abc");

        var json = ReportStore.Serialize(report);
        var back = ReportStore.Deserialize(json);

        Assert.Contains($"\"schemaVersion\": {CapabilityReport.CurrentSchemaVersion}", json, StringComparison.Ordinal);
        Assert.Contains("\"verdict\": \"Viable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"qsv\"", json, StringComparison.Ordinal);
        Assert.Contains("\"hevc_10bit\": \"Pass\"", json, StringComparison.Ordinal);
        Assert.NotNull(back);
        Assert.Equal(json, ReportStore.Serialize(back));
    }

    /// <summary>Another schema version or broken JSON reads as null.</summary>
    /// <param name="json">The input.</param>
    [Theory]
    [InlineData("{\"schemaVersion\": 1}")]
    [InlineData("not json")]
    public void IncompatibleInputIsNull(string json) => Assert.Null(ReportStore.Deserialize(json));

    /// <summary>Builds a small report.</summary>
    /// <param name="fingerprint">Its fingerprint.</param>
    /// <returns>The report.</returns>
    internal static CapabilityReport Sample(string fingerprint) => Reports.With(
        [
            Reports.Backend(
                HwType.qsv,
                "/dev/dri/renderD128",
                tier: PipelineTier.FullOpencl,
                decode: new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["hevc_10bit"] = ProbeOutcome.Pass },
                encode: new Dictionary<string, ProbeOutcome> { ["h264_lowpower"] = ProbeOutcome.Pass },
                tonemap: new Dictionary<string, ProbeOutcome> { ["opencl"] = ProbeOutcome.Pass },
                deinterlace: new Dictionary<string, ProbeOutcome> { ["qsv"] = ProbeOutcome.Pass },
                subtitles: new Dictionary<string, ProbeOutcome> { ["text"] = ProbeOutcome.Pass }),
        ],
        new FfmpegSummary("/usr/lib/jellyfin-ffmpeg/ffmpeg", "KnownPath", "7.1.4", IsJellyfinBuild: true),
        new HostSummary("linux", "6.8.0", "docker"),
        new StageASummary(
            ["vaapi", "qsv"],
            new Dictionary<HwType, BuildStatus> { [HwType.qsv] = BuildStatus.Selectable, [HwType.amf] = BuildStatus.NotBuilt },
            new Dictionary<string, bool> { ["tonemap_opencl.bt2390"] = true }),
        [new Finding(FindingSeverity.Warn, "legacy-copyback", "Install intel-opencl-icd.")],
        [new ProbeResult("qsv:/dev/dri/renderD128:Smoke:h264", HwType.qsv, "/dev/dri/renderD128", "h264", ProbeStage.Smoke, ProbeOutcome.Pass, 120.5, TimeSpan.FromSeconds(1), string.Empty, string.Empty)],
        fingerprint,
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
}
