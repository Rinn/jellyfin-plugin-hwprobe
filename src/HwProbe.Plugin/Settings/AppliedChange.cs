namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>One setting HwProbe changed.</summary>
/// <param name="Setting">The setting key.</param>
/// <param name="OldValue">The value before.</param>
/// <param name="NewValue">The value after.</param>
public sealed record AppliedChange(string Setting, string OldValue, string NewValue);
