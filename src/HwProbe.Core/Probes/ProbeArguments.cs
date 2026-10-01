namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Arguments generated for one probe, plus the environment the child must run with.</summary>
/// <param name="InputArgs">Device init and decoder arguments, placed before <c>-i</c>.</param>
/// <param name="FilterArgs">The video filter argument (<c>-vf</c>/<c>-filter_complex</c>), possibly empty.</param>
/// <param name="VideoEncoder">The encoder upstream selected, e.g. <c>h264_vaapi</c> or <c>libx264</c>.</param>
/// <param name="Environment">Variables set during generation, to pass to the child explicitly.</param>
public sealed record ProbeArguments(
    string InputArgs,
    string FilterArgs,
    string VideoEncoder,
    IReadOnlyDictionary<string, string?> Environment)
{
    /// <summary>Gets the hardware decoder upstream selected, or null when it decodes in software.</summary>
    public string? HardwareDecoder { get; init; }

    /// <summary>Gets a value indicating whether upstream picked a hardware encoder rather than its software fallback.</summary>
    public bool HardwareEncoder { get; init; }

    /// <summary>Gets a value indicating whether enabling hardware tone-mapping changes upstream's filter chain.</summary>
    public bool HardwareTonemap { get; init; }
}
