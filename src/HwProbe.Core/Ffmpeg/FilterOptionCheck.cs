namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>One <c>-h filter=</c> option check.</summary>
/// <param name="Key">The upstream <c>FilterOptionType</c> member name, so M2 maps it with <c>Enum.Parse</c>.</param>
/// <param name="Filter">The filter to ask about.</param>
/// <param name="RequiredText">Text in the help that proves the option exists.</param>
public sealed record FilterOptionCheck(string Key, string Filter, string RequiredText)
{
    /// <summary>Gets every check upstream runs; <c>EncoderValidator._filterOptionsDict</c> (v12.2).</summary>
    public static IReadOnlyList<FilterOptionCheck> All { get; } =
    [
        new("ScaleCudaFormat", "scale_cuda", "format"),
        new("TonemapCudaName", "tonemap_cuda", "GPU accelerated HDR to SDR tonemapping"),
        new("TonemapOpenclBt2390", "tonemap_opencl", "bt2390"),
        new("OverlayOpenclFrameSync", "overlay_opencl", "Action to take when encountering EOF from secondary input"),
        new("OverlayVaapiFrameSync", "overlay_vaapi", "Action to take when encountering EOF from secondary input"),
        new("OverlayVulkanFrameSync", "overlay_vulkan", "Action to take when encountering EOF from secondary input"),
        new("TransposeOpenclReversal", "transpose_opencl", "rotate by half-turn"),
        new("OverlayOpenclAlphaFormat", "overlay_opencl", "alpha_format"),
        new("OverlayCudaAlphaFormat", "overlay_cuda", "alpha_format"),
    ];
}
