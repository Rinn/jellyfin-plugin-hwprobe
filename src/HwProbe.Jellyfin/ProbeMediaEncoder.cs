using System.Globalization;
using Jellyfin.Extensions;
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
            ["SupportsHwaccel"] = a => capabilities.Hwaccels.Contains(Text(a[0])),
            ["SupportsEncoder"] = a => capabilities.Encoders.Contains(Text(a[0])),
            ["SupportsDecoder"] = a => capabilities.Decoders.Contains(Text(a[0])),
            ["SupportsFilter"] = a => capabilities.Filters.Contains(Text(a[0])),

            // Same escaping as MediaEncoder.EscapeSubtitleFilterPath (v12.2, L1224); the server uses the real one.
            ["EscapeSubtitleFilterPath"] = a => Text(a[0])
                .Replace('\\', '/')
                .Replace(":", "\\:", StringComparison.Ordinal)
                .Replace("'", @"'\\\''", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal),

            // MediaEncoder.GetInputPathArgument through EncodingUtils.GetFileInputArgument (v12.2): a library path is escaped, so its quotes can't end the argument.
            ["GetInputPathArgument"] = a => InputPath(a[0] is EncodingJobInfo job ? job.MediaPath : Text(a[0])),
            ["SupportsFilterWithOption"] = a => a[0] is FilterOptionType option && capabilities.FilterOptions.Contains(option),
            ["get_IsVaapiDeviceInteliHD"] = _ => capabilities.IsVaapiDeviceInteliHD,
            ["get_IsVaapiDeviceInteli965"] = _ => capabilities.IsVaapiDeviceInteli965,
            ["get_IsVaapiDeviceAmd"] = _ => capabilities.IsVaapiDeviceAmd,
            ["get_IsVaapiDeviceSupportVulkanDrmInterop"] = _ => capabilities.IsVaapiDeviceSupportVulkanDrmInterop,
            ["get_IsVaapiDeviceSupportVulkanDrmModifier"] = _ => capabilities.IsVaapiDeviceSupportVulkanDrmModifier,
            ["get_IsVideoToolboxAv1DecodeAvailable"] = _ => capabilities.IsVideoToolboxAv1DecodeAvailable,
        };

        return RecordingProxy.Create<IMediaEncoder>(recorder, handlers);

        static string Text(object? value) => value as string ?? throw new ArgumentException("IMediaEncoder takes a string here.", nameof(value));
    }

    /// <summary>Quotes a source path for ffmpeg as EncodingUtils.GetFileInputArgument does (v12.2).</summary>
    /// <param name="path">The path or URL.</param>
    /// <returns>The input argument.</returns>
    private static string InputPath(string path) =>
        path.Contains("://", StringComparison.Ordinal)
            ? string.Format(CultureInfo.InvariantCulture, "\"{0}\"", path)
            : string.Format(CultureInfo.InvariantCulture, "file:\"{0}\"", path.EscapeProcessArgument());
}
