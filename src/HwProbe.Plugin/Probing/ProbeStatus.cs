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

    /// <summary>Gets what is running, or ran last.</summary>
    public ProbeActivity Activity { get; init; }

    /// <summary>Gets the speed measurements done so far, while one runs.</summary>
    public int? Done { get; init; }

    /// <summary>Gets the speed measurements in the run, while one runs.</summary>
    public int? Total { get; init; }

    /// <summary>Gets where the running speed run is, or null when none is running.</summary>
    public SpeedPhase? Phase { get; init; }

    /// <summary>Gets the test video being made or downloaded, e.g. <c>Downloading Animation: 5 of 14 MB</c>, while preparing.</summary>
    public string? Preparing { get; init; }

    /// <summary>Gets what a running probe is doing, e.g. <c>Testing vaapi: hevc-10bit</c>.</summary>
    public string? Step { get; init; }

    /// <summary>Gets the test suite step running, e.g. <c>Encoder presets: medium, 4 of 5</c>, or null.</summary>
    public string? Suite { get; init; }

    /// <summary>Gets the measurements a running test suite's earlier steps made, or null outside a suite.</summary>
    public int? SuiteDone { get; init; }

    /// <summary>Gets the measurements in the whole running test suite: each finished or running step's planned count, and an estimate for the rest; null outside a suite.</summary>
    public int? SuiteTotal { get; init; }

    /// <summary>Gets the seconds since the running speed run or test suite started, pauses included, from the server's clock.</summary>
    public int? ElapsedSeconds { get; init; }
}
