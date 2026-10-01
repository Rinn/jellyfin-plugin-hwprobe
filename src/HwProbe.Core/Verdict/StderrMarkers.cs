using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;

namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>The ffmpeg stderr strings the verdict rules key on, kept in one place.</summary>
public static class StderrMarkers
{
    /// <summary>Prefix of the line naming the VAAPI driver once the display opens.</summary>
    /// <remarks>libavutil/hwcontext_vaapi.c, vaapi_device_connect, at verbose. Read from source, not observed.</remarks>
    public const string VaapiDriverPrefix = "VAAPI driver: ";

    /// <summary>Strerror text for EACCES, printed when ffmpeg reports the errno.</summary>
    /// <remarks>VAAPI's render-node open (hwcontext_vaapi.c, vaapi_device_create) drops errno, so a denied node may not show this.</remarks>
    public static readonly IReadOnlyList<string> PermissionDenied = ["Permission denied"];

    /// <summary>Device creation and open failures.</summary>
    public static readonly IReadOnlyList<string> DeviceUnavailable =
    [
        "No device",

        // fftools/ffmpeg_hw.c, hw_device_init_from_string; then fftools/cmdutils.c for the option.
        // Both observed with homebrew ffmpeg 9.0.2 on a failed cuda init.
        "Device creation failed",
        "Failed to set value",

        // libavutil/hwcontext_vaapi.c, vaapi_device_create and vaapi_device_connect.
        "as DRM device node",
        "Cannot open a VA display",
        "Failed to initialise VAAPI connection",

        // Encoder and decoder opens with no device; observed with jellyfin-ffmpeg 8.1.2 in a container.
        "Could not find a valid device",
        "Failed to init MPP context",
    ];

    /// <summary>Filter-graph failures.</summary>
    public static readonly IReadOnlyList<string> FilterUnsupported =
    [
        "Impossible to convert",
        "No such filter",
        "Error initializing filter",
        "Error reinitializing filters",
    ];

    /// <summary>Codec failures, including a hwaccel that could not set up for the stream.</summary>
    public static readonly IReadOnlyList<string> CodecUnsupported =
    [
        "not supported",
        "Unknown encoder",
        "Unknown decoder",
        "Error while opening encoder",

        // libavcodec/decode.c, hwaccel_init. With exit 0 it means the decode fell back to software.
        "Failed setup for format",
    ];

    /// <summary>Driver names upstream matches in that line (MediaEncoder.cs, v12.1, L246-248).</summary>
    public static readonly IReadOnlyList<(string Name, VaapiDriver Driver)> VaapiDrivers =
    [
        ("Intel iHD driver", VaapiDriver.IntelIhd),
        ("Intel i965 driver", VaapiDriver.IntelI965),
        ("Mesa Gallium driver", VaapiDriver.Amd),
    ];

    /// <summary>Generic failure text; any occurrence rules out a pass.</summary>
    public static readonly IReadOnlyList<string> Generic = ["Failed to"];

    /// <summary>Returns the stderr lines that prove the decoder produced hardware frames.</summary>
    /// <param name="type">The backend.</param>
    /// <returns>Alternative strings, any one of which confirms; empty when there is no hardware frame format.</returns>
    /// <remarks>
    /// Three sources, because copy-back pipelines download frames before the filter graph and not every
    /// decoder logs a reinit line: libavcodec/decode.c's <c>Format X chosen by get_format()</c> (debug),
    /// the h264/hevc decoder's <c>pix_fmt: X</c> (verbose), and the graph input's <c>pixfmt:X</c>
    /// (libavfilter/buffersrc.c, verbose). All observed with homebrew ffmpeg 9.0.2 for videotoolbox_vld.
    /// A failed hwaccel init also logs the get_format line, then "Failed setup for format", which the
    /// evaluator treats as a failure. Formats are upstream's <c>-hwaccel_output_format</c> values.
    /// </remarks>
    public static IReadOnlyList<string> HardwareFrames(HwType type) =>
        HardwareFormat(type) is { } format ? [$"Format {format} chosen by get_format()", $"pix_fmt: {format}", $"pixfmt:{format}"] : [];

    /// <summary>Returns upstream's <c>-hwaccel_output_format</c> for a backend.</summary>
    /// <param name="type">The backend.</param>
    /// <returns>The format, or null for v4l2m2m (encoder-only upstream) and none.</returns>
    public static string? HardwareFormat(HwType type) => type switch
    {
        HwType.vaapi => "vaapi",
        HwType.qsv => "qsv",
        HwType.nvenc => "cuda",
        HwType.amf => "d3d11",
        HwType.videotoolbox => "videotoolbox_vld",
        HwType.rkmpp => "drm_prime",
        HwType.v4l2m2m or HwType.none => null,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
