namespace Jellyfin.Plugin.HwProbe.Core.Model;

/// <summary>The filter-pipeline tier upstream would select.</summary>
public enum PipelineTier
{
    /// <summary>VAAPI/QSV with OpenCL: hardware tonemap and overlay.</summary>
    FullOpencl,

    /// <summary>VAAPI with Vulkan/libplacebo (AMD).</summary>
    FullVulkan,

    /// <summary>VideoToolbox with Metal filters: hardware scale, tonemap and overlay (macOS).</summary>
    FullMetal,

    /// <summary>Scale and deinterlace only; no hardware tonemap.</summary>
    Limited,

    /// <summary>Frames round-trip through system memory for software filters.</summary>
    LegacyCopyBack,

    /// <summary>Not resolved.</summary>
    Unknown,
}
