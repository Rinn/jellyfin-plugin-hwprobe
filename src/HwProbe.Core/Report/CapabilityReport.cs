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
    public const int CurrentSchemaVersion = 3;

    /// <summary>Gets the version of HwProbe that wrote the report; <c>unknown</c> for reports from before it was recorded.</summary>
    public string HwProbeVersion { get; init; } = "unknown";

    /// <summary>Gets how long the probe took in seconds; 0 for reports from before it was recorded.</summary>
    public double Seconds { get; init; }

    /// <summary>Gets the version of this HwProbe build, as reports record it.</summary>
    public static string CurrentHwProbeVersion { get; } = typeof(CapabilityReport).Assembly.GetName().Version?.ToString() ?? "unknown";
}
