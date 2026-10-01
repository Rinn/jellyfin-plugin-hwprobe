using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Jellyfin.Plugin.HwProbe.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.HwProbe;

/// <summary>The HwProbe plugin: device-verified hardware transcode detection.</summary>
[SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Jellyfin's plugin template names the plugin class Plugin; keep the ecosystem convention.")]
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>The plugin ID; must never change once released.</summary>
    public const string PluginId = "6c1f2f6e-3a4b-4d8e-9f2a-7b5c8d1e0a93";

    /// <summary>Initializes a new instance of the <see cref="Plugin"/> class.</summary>
    /// <param name="applicationPaths">Server paths.</param>
    /// <param name="xmlSerializer">Serializer for the plugin configuration.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Gets the loaded plugin, for reading configuration outside DI.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc/>
    public override string Name => "HwProbe";

    /// <inheritdoc/>
    public override Guid Id => Guid.Parse(PluginId, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override string Description => "Tests which hardware transcoding backends and codecs actually work on this server.";

    /// <inheritdoc/>
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
        },
    ];
}
