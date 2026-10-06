using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.TestSupport;

/// <summary>Builds reports for tests, with empty defaults for whatever a test doesn't set.</summary>
internal static class Reports
{
    /// <summary>Builds a backend row.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device, empty for none.</param>
    /// <param name="verdict">The verdict.</param>
    /// <param name="tier">The pipeline tier.</param>
    /// <param name="decode">Decode cells, or null for none.</param>
    /// <param name="encode">Encode cells, or null for none.</param>
    /// <param name="tonemap">Tone-map cells, or null for none.</param>
    /// <param name="deinterlace">Deinterlace cells, or null for none.</param>
    /// <param name="subtitles">Subtitle cells, or null for none.</param>
    /// <param name="hint">The remedy, empty for none.</param>
    /// <returns>The row.</returns>
    public static BackendReport Backend(
        HwType type,
        string device = "",
        BackendVerdict verdict = BackendVerdict.Viable,
        PipelineTier tier = PipelineTier.Unknown,
        IReadOnlyDictionary<string, ProbeOutcome>? decode = null,
        IReadOnlyDictionary<string, ProbeOutcome>? encode = null,
        IReadOnlyDictionary<string, ProbeOutcome>? tonemap = null,
        IReadOnlyDictionary<string, ProbeOutcome>? deinterlace = null,
        IReadOnlyDictionary<string, ProbeOutcome>? subtitles = null,
        string hint = "") =>
        new(type, device, verdict, tier, decode ?? Cells(), encode ?? Cells(), tonemap ?? Cells(), deinterlace ?? Cells(), subtitles ?? Cells(), hint);

    /// <summary>Builds a report of the current schema.</summary>
    /// <param name="backends">The backend rows.</param>
    /// <param name="ffmpeg">The probed binary, or null for a jellyfin-ffmpeg 8.1.2 at <c>/ffmpeg</c>.</param>
    /// <param name="host">The host, or null for Linux 6.8 outside a container.</param>
    /// <param name="stageA">Build-time capabilities, or null for none.</param>
    /// <param name="findings">Findings, or null for none.</param>
    /// <param name="probes">Probes, or null for none.</param>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="generated">When it was produced, or null for the Unix epoch.</param>
    /// <returns>The report.</returns>
    public static CapabilityReport With(
        IReadOnlyList<BackendReport> backends,
        FfmpegSummary? ffmpeg = null,
        HostSummary? host = null,
        StageASummary? stageA = null,
        IReadOnlyList<Finding>? findings = null,
        IReadOnlyList<ProbeResult>? probes = null,
        string fingerprint = "f",
        DateTimeOffset? generated = null) =>
        new(
            CapabilityReport.CurrentSchemaVersion,
            generated ?? DateTimeOffset.UnixEpoch,
            fingerprint,
            ffmpeg ?? new FfmpegSummary("/ffmpeg", "CommandLine", "8.1.2", IsJellyfinBuild: true),
            host ?? new HostSummary("linux", "6.8", null),
            stageA ?? new StageASummary([], new Dictionary<HwType, BuildStatus>(), new Dictionary<string, bool>()),
            backends,
            findings ?? [],
            probes ?? []);

    /// <summary>Builds a minimal report from this HwProbe build with one viable VAAPI backend in Docker.</summary>
    /// <returns>The report.</returns>
    public static CapabilityReport Sample() =>
        With(
            [Backend(HwType.vaapi, "/dev/dri/renderD128", tier: PipelineTier.FullOpencl)],
            new FfmpegSummary("/usr/lib/jellyfin-ffmpeg/ffmpeg", "Server", "8.1.2", IsJellyfinBuild: true),
            new HostSummary("linux", "6.8.0", "docker"),
            fingerprint: "sha256:plugin") with
        {
            HwProbeVersion = CapabilityReport.CurrentHwProbeVersion,
        };

    /// <summary>Returns every setting key <see cref="SettingsAdvisor"/> advises, over every backend and host, with no results and with passing ones.</summary>
    /// <returns>The distinct keys.</returns>
    public static IReadOnlyList<string> AdvisedSettings()
    {
        bool[] containers = [false, true];
        Dictionary<string, ProbeOutcome>[] resultSets = [[], new() { ["h264"] = ProbeOutcome.Pass, ["any_bwdif"] = ProbeOutcome.Pass, ["h264_keyframes"] = ProbeOutcome.Pass }];
        return [.. (
            from type in Enum.GetValues<HwType>()
            from os in Enum.GetValues<HostOs>()
            from container in containers
            from results in resultSets
            from advice in SettingsAdvisor.For(Backend(type, decode: results, encode: results, tonemap: results, deinterlace: results, subtitles: results), new AdviceContext(os, container, OpenclUnavailable: false))
            select advice.Setting).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Returns an empty cell map.</summary>
    /// <returns>The map.</returns>
    private static Dictionary<string, ProbeOutcome> Cells() => [];
}
