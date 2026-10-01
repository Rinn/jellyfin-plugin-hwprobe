using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>Sample reports for tests.</summary>
internal static class Reports
{
    /// <summary>Builds a minimal report with one viable backend.</summary>
    /// <returns>The report.</returns>
    public static CapabilityReport Sample() => new(
        CapabilityReport.CurrentSchemaVersion,
        DateTimeOffset.UnixEpoch,
        "sha256:plugin",
        new FfmpegSummary("/usr/lib/jellyfin-ffmpeg/ffmpeg", "Server", "8.1.2", IsJellyfinBuild: true),
        new HostSummary("linux", "6.8.0", "docker"),
        new StageASummary([], new Dictionary<HwType, BuildStatus>(), new Dictionary<string, bool>()),
        [new BackendReport(HwType.vaapi, "/dev/dri/renderD128", BackendVerdict.Viable, PipelineTier.FullOpencl, new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), string.Empty)],
        [],
        []);
}
