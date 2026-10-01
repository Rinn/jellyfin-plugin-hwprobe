using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Configuration;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>Builds argument sources over the running server's own EncodingHelper dependencies.</summary>
/// <param name="appPaths">Application paths.</param>
/// <param name="mediaEncoder">The server's media encoder.</param>
/// <param name="subtitleEncoder">The server's subtitle encoder.</param>
/// <param name="configuration">Server configuration.</param>
/// <param name="configurationManager">Server configuration manager.</param>
/// <param name="pathManager">Server path manager.</param>
/// <param name="baseline">Environment values captured when the plugin loaded.</param>
public sealed class ServerArgumentSourceFactory(
    IApplicationPaths appPaths,
    IMediaEncoder mediaEncoder,
    ISubtitleEncoder subtitleEncoder,
    IConfiguration configuration,
    MediaBrowser.Common.Configuration.IConfigurationManager configurationManager,
    IPathManager pathManager,
    ServerEnvironmentBaseline baseline) : IArgumentSourceFactory
{
    /// <inheritdoc/>
    /// <remarks>The capabilities are ignored: the server's encoder already knows its build.</remarks>
    public IArgumentSource Create(FfmpegCapabilities capabilities, DeviceTraits traits)
    {
        ArgumentNullException.ThrowIfNull(traits);

        var encoder = TraitMediaEncoder.Create(mediaEncoder, traits);
        var helper = new ProbeEncodingHelper(appPaths, encoder, subtitleEncoder, configuration, configurationManager, pathManager);
        var rules = EnvironmentRules.InServer(baseline.Values, ServerOwnedVariables());
        return new ArgumentSource(helper, encoder, rules, new CallRecorder());
    }

    /// <summary>Returns the variables the server's own transcodes set, from its configuration and device.</summary>
    /// <returns>Variables and values; empty unless the server uses VAAPI on an i965 or AMD device.</returns>
    private IReadOnlyDictionary<string, string> ServerOwnedVariables()
    {
        // The server writes these only in its VAAPI branch, using its configured device's traits.
        var options = configurationManager.GetEncodingOptions();
        return options.HardwareAccelerationType == HardwareAccelerationType.vaapi
            ? EncodingHelperEnvironment.Predict(HwType.vaapi, mediaEncoder.IsVaapiDeviceInteliHD, mediaEncoder.IsVaapiDeviceInteli965, mediaEncoder.IsVaapiDeviceAmd)
            : new Dictionary<string, string>();
    }
}
