namespace Jellyfin.Plugin.HwProbe.Core.Pipeline;

/// <summary>Computes upstream's composite build gates from build capability lookups.</summary>
/// <remarks>Filter-option names are upstream <c>FilterOptionType</c> member names.</remarks>
public static class BuildGates
{
    /// <summary>Upstream's <c>IsVaapiFullSupported</c>.</summary>
    /// <param name="hwaccel">Reports whether the build lists a hwaccel.</param>
    /// <param name="filter">Reports whether the build has a filter.</param>
    /// <param name="filterOption">Reports whether a <c>FilterOptionType</c> check passed.</param>
    /// <returns>True when every VAAPI filter the full pipeline uses is present.</returns>
    public static bool VaapiFull(Func<string, bool> hwaccel, Func<string, bool> filter, Func<string, bool> filterOption)
    {
        ArgumentNullException.ThrowIfNull(hwaccel);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(filterOption);

        return hwaccel("drm")
            && hwaccel("vaapi")
            && filter("scale_vaapi")
            && filter("deinterlace_vaapi")
            && filter("tonemap_vaapi")
            && filter("procamp_vaapi")
            && filterOption("OverlayVaapiFrameSync")
            && filter("transpose_vaapi")
            && filter("hwupload_vaapi");
    }

    /// <summary>Upstream's <c>IsOpenclFullSupported</c>.</summary>
    /// <param name="hwaccel">Reports whether the build lists a hwaccel.</param>
    /// <param name="filter">Reports whether the build has a filter.</param>
    /// <param name="filterOption">Reports whether a <c>FilterOptionType</c> check passed.</param>
    /// <returns>True when OpenCL scale, BT.2390 tonemap and frame-sync overlay are present.</returns>
    public static bool OpenclFull(Func<string, bool> hwaccel, Func<string, bool> filter, Func<string, bool> filterOption)
    {
        ArgumentNullException.ThrowIfNull(hwaccel);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(filterOption);

        return hwaccel("opencl")
            && filter("scale_opencl")
            && filterOption("TonemapOpenclBt2390")
            && filterOption("OverlayOpenclFrameSync");
    }

    /// <summary>Upstream's <c>IsVulkanFullSupported</c>.</summary>
    /// <param name="hwaccel">Reports whether the build lists a hwaccel.</param>
    /// <param name="filter">Reports whether the build has a filter.</param>
    /// <param name="filterOption">Reports whether a <c>FilterOptionType</c> check passed.</param>
    /// <returns>True when libplacebo and the Vulkan scale, overlay, transpose and flip filters are present.</returns>
    public static bool VulkanFull(Func<string, bool> hwaccel, Func<string, bool> filter, Func<string, bool> filterOption)
    {
        ArgumentNullException.ThrowIfNull(hwaccel);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(filterOption);

        return hwaccel("vulkan")
            && filter("libplacebo")
            && filter("scale_vulkan")
            && filterOption("OverlayVulkanFrameSync")
            && filter("transpose_vulkan")
            && filter("flip_vulkan");
    }
}
