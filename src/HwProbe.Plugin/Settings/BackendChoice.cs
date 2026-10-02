namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>A backend and device to switch to, as requested by the plugin page.</summary>
/// <param name="Type">The backend's lowercase name, e.g. <c>qsv</c>.</param>
/// <param name="Device">The probed device, e.g. <c>/dev/dri/renderD128</c>.</param>
public sealed record BackendChoice(string Type, string Device);
