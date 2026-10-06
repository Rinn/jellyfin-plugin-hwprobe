using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Cli.Tests;

/// <summary>Golden output of <see cref="SummaryRenderer"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SummaryRendererTests
{
    /// <summary>Cells are grouped by outcome, failures keep only explaining stderr lines, and shared remedies print once.</summary>
    [Fact]
    public void CompactGolden()
    {
        var stderr = "[h264 @ 0x1] Format qsv chosen by get_format().\n[vf @ 0x2] No such filter: 'subtitles'\nframe=0\nError opening output files: Filter not found\n";
        var report = Reports.With(
            [
                Reports.Backend(
                    HwType.qsv,
                    "/dev/dri/renderD128",
                    tier: PipelineTier.FullOpencl,
                    decode: new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["av1"] = ProbeOutcome.CodecUnsupported, ["vc1"] = ProbeOutcome.Untested },
                    encode: new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass },
                    subtitles: new Dictionary<string, ProbeOutcome> { ["text"] = ProbeOutcome.FilterUnsupported }),
            ],
            new FfmpegSummary("/usr/lib/jellyfin-ffmpeg/ffmpeg", "EnvironmentVariable", "8.1.2", IsJellyfinBuild: true),
            new HostSummary("linux", "6.8.0", "docker"),
            new StageASummary([], new Dictionary<HwType, BuildStatus> { [HwType.qsv] = BuildStatus.Selectable, [HwType.amf] = BuildStatus.NotBuilt }, new Dictionary<string, bool>()),
            [new Finding(FindingSeverity.Info, "lowpower-available-h264", "qsv /dev/dri/renderD128: low-power h264 encoding works.")],
            [
                Probe("qsv:/dev/dri/renderD128:Matrix:Decode:av1", ProbeOutcome.CodecUnsupported, "No hardware AV1.", string.Empty),
                Probe("qsv:/dev/dri/renderD128:Matrix:Subtitles:text", ProbeOutcome.FilterUnsupported, "A filter failed.", stderr),
                Probe("qsv:/dev/dri/renderD128:Matrix:Decode:vc1", ProbeOutcome.Untested, "No hardware AV1.", string.Empty),
                Probe("qsv:/dev/dri/renderD128:Matrix:Decode:h264", ProbeOutcome.Pass, string.Empty, "lots of log\n"),
            ],
            "sha256:x");

        var expected = $"""
            hwprobe summary (report schema {CapabilityReport.CurrentSchemaVersion})
            ffmpeg  8.1.2, jellyfin-ffmpeg, /usr/lib/jellyfin-ffmpeg/ffmpeg (EnvironmentVariable)
            host    linux 6.8.0, in docker
            built   qsv; not built: amf

            qsv /dev/dri/renderD128: Viable, tier FullOpencl
              decode  ok: h264 | no: av1(CodecUnsupported) | untested: vc1
              encode  ok: h264
              subs    no: text(FilterUnsupported)

            findings
              INFO lowpower-available-h264: qsv /dev/dri/renderD128: low-power h264 encoding works.

            failures
              qsv:/dev/dri/renderD128:Matrix:Decode:av1: CodecUnsupported [1]
              qsv:/dev/dri/renderD128:Matrix:Subtitles:text: FilterUnsupported [2]
                > [vf @ 0x2] No such filter: 'subtitles'
              qsv:/dev/dri/renderD128:Matrix:Decode:vc1: Untested [1]

            remedies
              [1] No hardware AV1.
              [2] A filter failed.

            """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), SummaryRenderer.Render(report));
    }

    /// <summary>Builds a probe result.</summary>
    /// <param name="id">Probe ID.</param>
    /// <param name="outcome">Outcome.</param>
    /// <param name="hint">Remedy.</param>
    /// <param name="stderr">Stderr tail.</param>
    /// <returns>The probe.</returns>
    private static ProbeResult Probe(string id, ProbeOutcome outcome, string hint, string stderr) =>
        new(id, HwType.qsv, "/dev/dri/renderD128", null, ProbeStage.Matrix, outcome, null, TimeSpan.Zero, hint, stderr);
}
