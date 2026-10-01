using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Configuration;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>EncodingHelper with its protected hardware-decoder choice made public.</summary>
/// <param name="appPaths">Application paths.</param>
/// <param name="mediaEncoder">The media encoder.</param>
/// <param name="subtitleEncoder">The subtitle encoder.</param>
/// <param name="config">Configuration.</param>
/// <param name="configurationManager">Configuration manager.</param>
/// <param name="pathManager">Path manager.</param>
internal sealed class ProbeEncodingHelper(
    IApplicationPaths appPaths,
    IMediaEncoder mediaEncoder,
    ISubtitleEncoder subtitleEncoder,
    IConfiguration config,
    MediaBrowser.Common.Configuration.IConfigurationManager configurationManager,
    IPathManager pathManager)
    : EncodingHelper(appPaths, mediaEncoder, subtitleEncoder, config, configurationManager, pathManager)
{
    /// <summary>Returns the hardware decoder upstream would use for this job.</summary>
    /// <param name="state">The job.</param>
    /// <param name="options">Encoding options.</param>
    /// <returns>The decoder arguments, or null when it would decode in software.</returns>
    public string? HardwareDecoder(EncodingJobInfo state, EncodingOptions options)
    {
        var decoder = GetHardwareVideoDecoder(state, options);
        return string.IsNullOrEmpty(decoder) ? null : decoder;
    }
}
