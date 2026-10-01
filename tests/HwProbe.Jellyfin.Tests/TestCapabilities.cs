using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Capability sets for a fully featured build on each platform.</summary>
internal static class TestCapabilities
{
    /// <summary>Gets a jellyfin-ffmpeg-like build with every hwaccel, encoder and filter the backends need.</summary>
    public static ProbeCapabilities Full { get; } = new()
    {
        EncoderPath = "/usr/lib/jellyfin-ffmpeg/ffmpeg",
        EncoderVersion = new Version(7, 1, 4),
        Hwaccels = new HashSet<string> { "vaapi", "qsv", "drm", "opencl", "cuda", "vulkan", "videotoolbox", "d3d11va", "rkmpp" },
        Encoders = new HashSet<string>
        {
            "h264_vaapi", "hevc_vaapi", "av1_vaapi", "h264_qsv", "hevc_qsv", "av1_qsv", "h264_nvenc", "hevc_nvenc",
            "h264_amf", "hevc_amf", "h264_videotoolbox", "hevc_videotoolbox", "h264_rkmpp", "hevc_rkmpp", "h264_v4l2m2m",
        },
        Decoders = new HashSet<string> { "h264", "hevc", "vp9", "av1", "mpeg2video", "vc1" },
        Filters = new HashSet<string>
        {
            "scale_vaapi", "deinterlace_vaapi", "tonemap_vaapi", "procamp_vaapi", "transpose_vaapi", "hwupload_vaapi",
            "scale_opencl", "alphasrc", "scale_vt", "yadif_videotoolbox", "overlay_videotoolbox", "tonemap_videotoolbox",
        },
        FilterOptions = new HashSet<FilterOptionType>
        {
            FilterOptionType.TonemapOpenclBt2390,
            FilterOptionType.OverlayOpenclFrameSync,
            FilterOptionType.OverlayVaapiFrameSync,
        },
        IsVaapiDeviceInteliHD = true,
    };
}
