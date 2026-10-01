using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>Results for one (backend, device) row.</summary>
/// <param name="Type">The backend.</param>
/// <param name="Device">Device node or adapter index; empty when the backend takes none.</param>
/// <param name="Verdict">Device verdict.</param>
/// <param name="Tier">Resolved filter-pipeline tier.</param>
/// <param name="Decode">Decode cells by codec key, e.g. <c>hevc10</c>.</param>
/// <param name="Encode">Encode cells by codec key, e.g. <c>h264_lowpower</c>.</param>
/// <param name="Tonemap">Tone-map cells by method.</param>
/// <param name="Hint">Remedy for a non-viable verdict; empty otherwise.</param>
public sealed record BackendReport(
    HwType Type,
    string Device,
    BackendVerdict Verdict,
    PipelineTier Tier,
    IReadOnlyDictionary<string, ProbeOutcome> Decode,
    IReadOnlyDictionary<string, ProbeOutcome> Encode,
    IReadOnlyDictionary<string, ProbeOutcome> Tonemap,
    string Hint);
