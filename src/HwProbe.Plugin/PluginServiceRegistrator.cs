using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Jellyfin;
using Jellyfin.Plugin.HwProbe.Probing;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.HwProbe;

/// <summary>Registers the plugin's services with the server.</summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc/>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Runs at server start-up, before any transcode has written to these variables.
        serviceCollection.AddSingleton(new ServerEnvironmentBaseline(EncodingHelperEnvironment.Capture()));
        serviceCollection.AddSingleton<IArgumentSourceFactory, ServerArgumentSourceFactory>();
        serviceCollection.AddSingleton<ProbeService>();
    }
}
