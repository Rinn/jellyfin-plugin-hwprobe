namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>An option to set, as requested by the plugin page.</summary>
/// <param name="Setting">The setting key from the advice.</param>
/// <param name="Value">The value to set.</param>
public sealed record SettingChange(string Setting, bool Value);
