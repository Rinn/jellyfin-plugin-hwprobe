using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>Builds an <see cref="IMediaEncoder"/> that answers capability queries from <see cref="ProbeCapabilities"/>.</summary>
public static class ProbeMediaEncoder
{
    /// <summary>Creates the encoder; members outside capability queries throw and are recorded as unexpected.</summary>
    /// <param name="capabilities">The capabilities to report.</param>
    /// <param name="recorder">Receives every call.</param>
    /// <returns>The encoder.</returns>
    public static IMediaEncoder Create(ProbeCapabilities capabilities, CallRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var handlers = new Dictionary<string, Func<object?[], object?>>
        {
            ["get_EncoderPath"] = _ => capabilities.EncoderPath,
            ["get_EncoderVersion"] = _ => capabilities.EncoderVersion,
            ["SupportsHwaccel"] = a => capabilities.Hwaccels.Contains((string)a[0]!),
            ["SupportsEncoder"] = a => capabilities.Encoders.Contains((string)a[0]!),
            ["SupportsDecoder"] = a => capabilities.Decoders.Contains((string)a[0]!),
            ["SupportsFilter"] = a => capabilities.Filters.Contains((string)a[0]!),

            // Same escaping as MediaEncoder.EscapeSubtitleFilterPath (v12.1, L1224); the server uses the real one.
            ["EscapeSubtitleFilterPath"] = a => ((string)a[0]!)
                .Replace('\\', '/')
                .Replace(":", "\\:", StringComparison.Ordinal)
                .Replace("'", @"'\\\''", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal),
            ["SupportsFilterWithOption"] = a => capabilities.FilterOptions.Contains((FilterOptionType)a[0]!),
            ["get_IsVaapiDeviceInteliHD"] = _ => capabilities.IsVaapiDeviceInteliHD,
            ["get_IsVaapiDeviceInteli965"] = _ => capabilities.IsVaapiDeviceInteli965,
            ["get_IsVaapiDeviceAmd"] = _ => capabilities.IsVaapiDeviceAmd,
            ["get_IsVaapiDeviceSupportVulkanDrmInterop"] = _ => capabilities.IsVaapiDeviceSupportVulkanDrmInterop,
            ["get_IsVaapiDeviceSupportVulkanDrmModifier"] = _ => capabilities.IsVaapiDeviceSupportVulkanDrmModifier,
            ["get_IsVideoToolboxAv1DecodeAvailable"] = _ => capabilities.IsVideoToolboxAv1DecodeAvailable,
        };

        return RecordingProxy.Create<IMediaEncoder>(recorder, handlers);
    }
}
