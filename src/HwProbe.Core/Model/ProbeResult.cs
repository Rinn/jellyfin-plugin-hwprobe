namespace Jellyfin.Plugin.HwProbe.Core.Model;

/// <summary>One executed (or skipped) probe.</summary>
/// <param name="ProbeId">Stable identifier, e.g. <c>vaapi:/dev/dri/renderD128:C:decode:hevc10</c>.</param>
/// <param name="Type">The backend probed.</param>
/// <param name="DevicePath">Render node, adapter index, or empty when the backend takes none.</param>
/// <param name="Codec">The codec cell, or null for device-level probes.</param>
/// <param name="Stage">The pipeline stage.</param>
/// <param name="Outcome">The classified outcome.</param>
/// <param name="Fps">Reported throughput, when available.</param>
/// <param name="Duration">Wall-clock time of the probe.</param>
/// <param name="Hint">Remedy text for a failure; empty on pass.</param>
/// <param name="StderrTail">The last ~4 KB of stderr.</param>
public sealed record ProbeResult(
    string ProbeId,
    HwType Type,
    string DevicePath,
    string? Codec,
    ProbeStage Stage,
    ProbeOutcome Outcome,
    double? Fps,
    TimeSpan Duration,
    string Hint,
    string StderrTail)
{
    /// <summary>Gets the ffmpeg argument string, or null when nothing was launched.</summary>
    public string? CommandLine { get; init; }
}
