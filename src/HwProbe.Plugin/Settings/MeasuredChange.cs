namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>A setting a performance test suggested, as requested by the plugin page.</summary>
/// <param name="Setting">The catalog option key, e.g. <c>EncoderPreset</c>.</param>
/// <param name="Value">The value, as the catalog keys it.</param>
public sealed record MeasuredChange(string Setting, string Value)
{
    /// <summary>Gets the run the suggestion was drawn for, or null for the latest.</summary>
    public string? Run { get; init; }
}
