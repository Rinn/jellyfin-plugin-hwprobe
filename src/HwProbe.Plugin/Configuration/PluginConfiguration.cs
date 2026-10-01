using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HwProbe.Configuration;

/// <summary>Plugin settings.</summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the per-probe hard timeout, in seconds.</summary>
    public int ProbeTimeoutSeconds { get; set; } = 15;

    /// <summary>Gets or sets the fixture generation timeout, in seconds.</summary>
    public int FixtureTimeoutSeconds { get; set; } = 120;
}
