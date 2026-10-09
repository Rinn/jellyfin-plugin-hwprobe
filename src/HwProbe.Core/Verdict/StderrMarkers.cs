using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;

namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>The ffmpeg stderr strings the verdict rules key on, kept in one place.</summary>
public static class StderrMarkers
{
    /// <summary>The line the V4L2 encoder logs when it opens its device (libavcodec/v4l2_m2m.c), confirming v4l2m2m's only hardware step.</summary>
    public const string V4l2Device = "] Using device /dev/video";

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

    /// <summary>The encoder dropped low-power mode and encoded without it.</summary>
    /// <remarks>Observed from hevc_qsv with jellyfin-ffmpeg 8.1.2 on Gen 9 graphics, which have no low-power HEVC; exit 0 with frames.</remarks>
    public static readonly IReadOnlyList<string> LowPowerDisabled = ["not supported under Low power mode"];

    /// <summary>The hwaccel could not start for this stream, so decoding fell back to software.</summary>
    /// <remarks>libavcodec/decode.c, hwaccel_init. Observed with jellyfin-ffmpeg 8.1.2 for AV1 on a GPU without AV1 decode.</remarks>
    public static readonly IReadOnlyList<string> HwaccelSetupFailed = ["Failed setup for format"];

    /// <summary>Driver names upstream matches in that line (MediaEncoder.cs, v12.2, L246-248).</summary>
    public static readonly IReadOnlyList<(string Name, VaapiDriver Driver)> VaapiDrivers =
    [
        ("Intel iHD driver", VaapiDriver.IntelIhd),
        ("Intel i965 driver", VaapiDriver.IntelI965),
        ("Mesa Gallium driver", VaapiDriver.Amd),
    ];

    /// <summary>Generic failure text; any occurrence rules out a pass.</summary>
    public static readonly IReadOnlyList<string> Generic = ["Failed to"];

    /// <summary>Lines that match a failure marker but don't affect the transcode; removed before matching.</summary>
    /// <remarks>
    /// Stream probing's own decoder open (libavformat, avformat_find_stream_info). Observed with jellyfin-ffmpeg
    /// 8.1.3 on a VC-1 elementary stream that then decoded in hardware. VideoToolbox declining an optional speed
    /// hint, logged as a warning before encoding carries on (libavcodec/videotoolboxenc.c). Observed with
    /// jellyfin-ffmpeg 8.1.3 for mjpeg_videotoolbox on Apple silicon. A V4L2 encoder rejecting optional frame-level
    /// rate control (libavcodec/v4l2_m2m_enc.c), and its device search skipping devices that don't fit
    /// (libavcodec/v4l2_m2m.c), both observed with ffmpeg 7.1.5 on a Raspberry Pi's bcm2835-codec.
    /// jellyfin-ffmpeg's RKMPP encoder logging EAGAIN (-11) at debug level when its output queue is empty or its
    /// input queue full, then trying again (debian/patches/0042, rkmppenc.c); the first observed with
    /// jellyfin-ffmpeg 8.1.3 on an RK3588S.
    /// </remarks>
    public static readonly IReadOnlyList<string> Harmless =
    [
        "Failed to open codec in avformat_find_stream_info",
        "PrioritizeEncodingSpeedOverQuality property is not supported on this device. Ignoring.",
        "Failed to set frame level rate control: Invalid argument",
        "v4l2 capture format not supported",
        "v4l2 output format not supported",
        "Failed to get packet from encoder output queue: -11",
        "Failed to put frame to encoder input queue: -11",
    ];

    /// <summary>Lines ffmpeg prints as a failure spreads through its threads and outputs, which name no cause, so a failure note skips them for the line that does.</summary>
    /// <remarks>
    /// fftools (ffmpeg 7 and 8): an encoder that fails to open logs its own error, then "Error while opening encoder", "Error sending
    /// frames to consumers", "Could not open encoder before EOF", "Task finished with error code", "Terminating thread", and "Nothing was
    /// written into output file"; observed with aac_at on 96 kHz FLAC (Homebrew ffmpeg 9.0.2) and mjpeg on NVIDIA (jellyfin-ffmpeg 8.1.3).
    /// </remarks>
    public static readonly IReadOnlyList<string> Consequences =
    [
        "Task finished with error code",
        "Terminating thread with return code",
        "Nothing was written into output file",
        "Could not open encoder before EOF",
        "Error sending frames to consumers",
        "Error while opening encoder",
        "Error submitting",
        "Error encoding a frame",
        "Error selecting an encoder",
        "Error opening output file",
        "Conversion failed!",
    ];

    /// <summary>Gets every failure marker, for picking the stderr lines that explain a failure.</summary>
    public static IReadOnlyList<string> AllFailures { get; } = [.. PermissionDenied, .. DeviceUnavailable, .. FilterUnsupported, .. CodecUnsupported, .. Generic];

    /// <summary>Returns the stderr lines that prove the decoder produced hardware frames, or for v4l2m2m that the encoder opened its device.</summary>
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
        type == HwType.v4l2m2m ? [V4l2Device] : Confirmations(HardwareFormat(type));

    /// <summary>Returns stderr strings confirming hardware frames for a backend or for the hwaccel its arguments use.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="hwaccel">The <c>-hwaccel</c> value in the generated arguments, or null.</param>
    /// <returns>Alternative strings, any one of which confirms.</returns>
    public static IReadOnlyList<string> HardwareFrames(HwType type, string? hwaccel) =>
        [.. HardwareFrames(type).Union(Confirmations(hwaccel is null ? null : HwaccelFormat(hwaccel)), StringComparer.Ordinal)];

    /// <summary>Returns the frame format a <c>-hwaccel</c> decodes to, as paired in upstream's decoder arguments.</summary>
    /// <param name="hwaccel">The <c>-hwaccel</c> value.</param>
    /// <returns>The format, or null for an unknown hwaccel.</returns>
    /// <remarks>EncodingHelper.GetHwaccelType pairs each <c>-hwaccel</c> with its <c>-hwaccel_output_format</c>.</remarks>
    public static string? HwaccelFormat(string hwaccel) => hwaccel switch
    {
        "vaapi" => "vaapi",
        "qsv" => "qsv",
        "cuda" => "cuda",
        "d3d11va" => "d3d11",
        "videotoolbox" => "videotoolbox_vld",
        "rkmpp" => "drm_prime",
        _ => null,
    };

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

    /// <summary>Returns the three stderr strings that confirm frames in a format.</summary>
    /// <param name="format">The hardware frame format, or null.</param>
    /// <returns>The strings; empty for null.</returns>
    private static IReadOnlyList<string> Confirmations(string? format) =>
        format is null ? [] : [$"Format {format} chosen by get_format()", $"pix_fmt: {format}", $"pixfmt:{format}"];
}
