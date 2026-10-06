namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>A choice in a setting group a performance test measured, as requested by the plugin page.</summary>
/// <param name="Group">The catalog setting group's key, e.g. <c>deinterlace</c>.</param>
/// <param name="Row">The row's label.</param>
public sealed record MeasuredRow(string Group, string Row)
{
    /// <summary>Gets the run the suggestions were drawn for, or null for the latest.</summary>
    public string? Run { get; init; }
}
