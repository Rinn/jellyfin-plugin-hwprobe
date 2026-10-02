using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>The probe runner's current state and last outcome.</summary>
/// <param name="State">Whether a probe is running.</param>
/// <param name="LastStartedUtc">When the last probe started, or null.</param>
/// <param name="LastCompletedUtc">When the last probe finished, or null.</param>
/// <param name="LastError">Why the last probe failed, or null.</param>
public sealed record ProbeStatus(ProbeState State, DateTimeOffset? LastStartedUtc, DateTimeOffset? LastCompletedUtc, string? LastError)
{
    /// <summary>Gets the running HwProbe version, so an open page can tell the plugin was updated under it.</summary>
    public string HwProbeVersion { get; init; } = CapabilityReport.CurrentHwProbeVersion;
}
