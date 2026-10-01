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

    /// <summary>Gets the <c>-hwaccel</c> named in <see cref="InputArgs"/>, or null when there is none.</summary>
    /// <remarks>Not always the backend's own: Jellyfin decodes QSV through VAAPI when native decoders are preferred.</remarks>
    public string? Hwaccel
    {
        get
        {
            var tokens = InputArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var index = Array.IndexOf(tokens, "-hwaccel");
            return index >= 0 && index + 1 < tokens.Length ? tokens[index + 1] : null;
        }
    }

    /// <summary>Gets the hardware filter family that deinterlaces, e.g. <c>vaapi</c>, or null when it's done on the CPU or not at all.</summary>
    public string? HardwareDeinterlacer { get; init; }
}
