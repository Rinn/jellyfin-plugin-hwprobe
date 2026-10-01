using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Cli.Tests;

/// <summary>Golden output of <see cref="TableRenderer"/>.</summary>
[Trait("Category", "Unit")]
public sealed class TableRendererTests
{
    /// <summary>A mixed pass/fail host renders the documented table.</summary>
    [Fact]
    public void MixedHostGolden()
    {
        var report = new CapabilityReport(
            CapabilityReport.CurrentSchemaVersion,
            DateTimeOffset.UnixEpoch,
            "sha256:x",
            new FfmpegSummary("/usr/bin/ffmpeg", "SystemPath", "7.1.1", IsJellyfinBuild: false),
            new HostSummary("linux", "6.8.0", "docker"),
            new StageASummary([], new Dictionary<HwType, BuildStatus> { [HwType.amf] = BuildStatus.NotBuilt, [HwType.qsv] = BuildStatus.Selectable }, new Dictionary<string, bool>()),
            [
                new BackendReport(
                    HwType.vaapi,
                    "/dev/dri/renderD128",
                    BackendVerdict.Viable,
                    PipelineTier.LegacyCopyBack,
                    new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["av1"] = ProbeOutcome.CodecUnsupported, ["vc1"] = ProbeOutcome.Untested },
                    new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass },
                    new Dictionary<string, ProbeOutcome>(),
                    string.Empty),
                new BackendReport(HwType.nvenc, "0", BackendVerdict.NotPresent, PipelineTier.Unknown, new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), "ffmpeg has CUDA but no NVIDIA device was found."),
            ],
            [new Finding(FindingSeverity.Warn, "legacy-copyback", "vaapi is on the copy-back path; install intel-opencl-icd.")],
            []);

        var expected = """
            ffmpeg  /usr/bin/ffmpeg (7.1.1, SystemPath)
                    warning: not a jellyfin-ffmpeg build; results may differ from the server's
            host    linux 6.8.0 (docker)
            build   not built: amf

            TYPE   DEVICE               VERDICT     TIER            DECODE        ENCODE  TONEMAP
            vaapi  /dev/dri/renderD128  Viable      LegacyCopyBack  1/2 (no av1)  1/1     -
            nvenc  0                    NotPresent  -               -             -       -

            nvenc 0: ffmpeg has CUDA but no NVIDIA device was found.

            WARN legacy-copyback: vaapi is on the copy-back path; install intel-opencl-icd.

            """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), TableRenderer.Render(report, verbose: false));
    }

    /// <summary>Verbose output lists each probe with a runnable command line, hint and stderr tail.</summary>
    [Fact]
    public void VerboseListsCommandLines()
    {
        var probe = new ProbeResult("vaapi:/dev/dri/renderD128:DeviceOpen", HwType.vaapi, "/dev/dri/renderD128", null, ProbeStage.DeviceOpen, ProbeOutcome.DeviceUnavailable, null, TimeSpan.FromSeconds(0.2), "Pass the render node in.", "Device creation failed: -5.\n")
        {
            CommandLine = "-v verbose -hide_banner -init_hw_device vaapi=va:/dev/dri/renderD128",
        };
        var report = new CapabilityReport(
            CapabilityReport.CurrentSchemaVersion,
            DateTimeOffset.UnixEpoch,
            "sha256:x",
            new FfmpegSummary("/usr/lib/jellyfin-ffmpeg/ffmpeg", "KnownPath", "8.1.2", IsJellyfinBuild: true),
            new HostSummary("linux", "6.8.0", null),
            new StageASummary([], new Dictionary<HwType, BuildStatus>(), new Dictionary<string, bool>()),
            [],
            [],
            [probe]);

        var text = TableRenderer.Render(report, verbose: true);

        Assert.Contains(
            "\n[DeviceUnavailable] vaapi:/dev/dri/renderD128:DeviceOpen (0.2s)\n$ /usr/lib/jellyfin-ffmpeg/ffmpeg -v verbose -hide_banner -init_hw_device vaapi=va:/dev/dri/renderD128\nhint: Pass the render node in.\nDevice creation failed: -5.\n",
            text,
            StringComparison.Ordinal);
    }
}
