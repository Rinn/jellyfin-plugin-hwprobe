using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>The complete probe report, as written to JSON.</summary>
/// <param name="SchemaVersion">Report format version; bump on breaking changes.</param>
/// <param name="GeneratedUtc">When the report was produced.</param>
/// <param name="Fingerprint">Cache key for this host + ffmpeg.</param>
/// <param name="Ffmpeg">The probed binary.</param>
/// <param name="Host">The host.</param>
/// <param name="StageA">Build-time capabilities.</param>
/// <param name="Backends">One row per (backend, device).</param>
/// <param name="Findings">Report-level observations.</param>
/// <param name="Probes">Every probe, for debugging.</param>
public sealed record CapabilityReport(
    int SchemaVersion,
    DateTimeOffset GeneratedUtc,
    string Fingerprint,
    FfmpegSummary Ffmpeg,
    HostSummary Host,
    StageASummary StageA,
    IReadOnlyList<BackendReport> Backends,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<ProbeResult> Probes)
{
    /// <summary>The schema version this build writes.</summary>
    public const int CurrentSchemaVersion = 1;
}
