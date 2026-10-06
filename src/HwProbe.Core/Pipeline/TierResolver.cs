using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Pipeline;

/// <summary>Predicts which filter pipeline upstream selects for a VAAPI or QSV job.</summary>
/// <remarks>Mirrors <c>GetVaapiVidFilterChain</c> and <c>GetIntelVidFilterChain</c> in Jellyfin 12.2 <c>EncodingHelper</c>.</remarks>
public static class TierResolver
{
    /// <summary>Upstream's <c>_minKernelVersionAmdVkFmtModifier</c>.</summary>
    public static readonly Version MinKernelVersionAmdVkFmtModifier = new(5, 15);

    /// <summary>Resolves the pipeline tier.</summary>
    /// <param name="gates">The upstream inputs.</param>
    /// <returns>The tier; <see cref="PipelineTier.Unknown"/> only for backends other than VAAPI and QSV.</returns>
    public static PipelineTier Resolve(TierGates gates)
    {
        ArgumentNullException.ThrowIfNull(gates);

        return gates.Backend switch
        {
            HwType.vaapi => ResolveVaapi(gates),
            HwType.qsv => ResolveQsv(gates),
            _ => PipelineTier.Unknown,
        };
    }

    /// <summary>Follows <c>GetVaapiVidFilterChain</c>.</summary>
    /// <param name="gates">The upstream inputs.</param>
    /// <returns>The VAAPI tier.</returns>
    private static PipelineTier ResolveVaapi(TierGates gates)
    {
        var vaapiFull = gates.Platform == HostOs.Linux && IsVaapiSupported(gates) && gates.VaapiFull;

        // OpenCL and alphasrc gate every preferred VAAPI path, the AMD Vulkan one included.
        if (!gates.HasHardwareCodec || !(vaapiFull && gates.OpenclFull) || !gates.Alphasrc)
        {
            return PipelineTier.LegacyCopyBack;
        }

        if (gates.Driver == VaapiDriver.IntelIhd)
        {
            return PipelineTier.FullOpencl;
        }

        if (gates.Driver == VaapiDriver.Amd
            && gates.VulkanFull
            && gates.VulkanDrmInterop
            && gates.KernelVersion >= MinKernelVersionAmdVkFmtModifier)
        {
            return PipelineTier.FullVulkan;
        }

        return PipelineTier.Limited;
    }

    /// <summary>Follows <c>GetIntelVidFilterChain</c>.</summary>
    /// <param name="gates">The upstream inputs.</param>
    /// <returns>The QSV tier.</returns>
    private static PipelineTier ResolveQsv(TierGates gates)
    {
        var qsvOcl = gates.HwaccelQsv && gates.OpenclFull;
        var intelVaapiOcl = gates.Platform == HostOs.Linux && IsVaapiSupported(gates) && qsvOcl;
        var intelDx11Ocl = gates.Platform == HostOs.Windows && gates.HwaccelD3d11va && qsvOcl;

        if (!gates.HasHardwareCodec || !(intelVaapiOcl || intelDx11Ocl) || !gates.Alphasrc)
        {
            return PipelineTier.LegacyCopyBack;
        }

        // QSV-over-VAAPI and QSV-over-D3D11 both filter in OpenCL.
        return PipelineTier.FullOpencl;
    }

    /// <summary>Upstream's <c>IsVaapiSupported</c>.</summary>
    /// <param name="gates">The upstream inputs.</param>
    /// <returns>True when the build has VAAPI and the input isn't MPEG-4 Part 2.</returns>
    private static bool IsVaapiSupported(TierGates gates) => gates.HwaccelVaapi && !gates.InputIsMpeg4;
}
