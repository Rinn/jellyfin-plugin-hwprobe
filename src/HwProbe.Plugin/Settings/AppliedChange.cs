namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>One setting HwProbe changed.</summary>
/// <param name="Setting">The setting key.</param>
/// <param name="OldValue">The value before.</param>
/// <param name="NewValue">The value after.</param>
public sealed record AppliedChange(string Setting, string OldValue, string NewValue)
{
    /// <summary>Gets the option's label as the settings list showed it, or null for changes recorded without one.</summary>
    /// <remarks>Kept so the page can name the change after the report it came from is gone, e.g. after an update.</remarks>
    public string? Label { get; init; }
}
