using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Every speed result from one run, as written to JSON.</summary>
/// <param name="GeneratedUtc">When the run finished.</param>
/// <param name="Ffmpeg">The ffmpeg measured.</param>
/// <param name="Method">How streams were counted.</param>
/// <param name="Results">One row per backend, test and variant.</param>
public sealed record SpeedReport(DateTimeOffset GeneratedUtc, FfmpegSummary Ffmpeg, SpeedMethod Method, IReadOnlyList<SpeedResult> Results)
{
    /// <summary>Gets the version of HwProbe that measured it.</summary>
    public string HwProbeVersion { get; init; } = CapabilityReport.CurrentHwProbeVersion;
}
