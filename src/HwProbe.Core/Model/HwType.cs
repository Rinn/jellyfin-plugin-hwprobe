using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.HwProbe.Core.Model;

/// <summary>Hardware acceleration backends; values and lowercase names mirror upstream <c>HardwareAccelerationType</c>.</summary>
[SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:Element should begin with upper-case letter", Justification = "Mirrors upstream's lowercase enum so M2 maps without a translation table.")]
public enum HwType
{
    /// <summary>Software only.</summary>
    none = 0,

    /// <summary>AMD AMF (Windows).</summary>
    amf = 1,

    /// <summary>Intel Quick Sync Video.</summary>
    qsv = 2,

    /// <summary>NVIDIA NVENC/NVDEC via CUDA.</summary>
    nvenc = 3,

    /// <summary>V4L2 mem-to-mem; encoder only upstream.</summary>
    v4l2m2m = 4,

    /// <summary>VA-API (Linux).</summary>
    vaapi = 5,

    /// <summary>Apple VideoToolbox.</summary>
    videotoolbox = 6,

    /// <summary>Rockchip MPP.</summary>
    rkmpp = 7,
}
