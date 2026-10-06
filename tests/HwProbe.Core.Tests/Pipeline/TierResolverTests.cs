using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Pipeline;

/// <summary>Tier selection, checked against upstream's VAAPI and QSV dispatch.</summary>
[Trait("Category", "Unit")]
public sealed class TierResolverTests
{
    private static readonly Version _oldKernel = new(5, 14);
    private static readonly Version _newKernel = new(5, 15);

    /// <summary>An Intel iHD host with everything present.</summary>
    private static readonly TierGates _intelIhd = new(
        HwType.vaapi,
        HostOs.Linux,
        HasHardwareCodec: true,
        InputIsMpeg4: false,
        HwaccelVaapi: true,
        HwaccelQsv: true,
        HwaccelD3d11va: false,
        VaapiFull: true,
        OpenclFull: true,
        VulkanFull: true,
        Alphasrc: true,
        VaapiDriver.IntelIhd,
        VulkanDrmInterop: true,
        _newKernel);

    /// <summary>An AMD radeonsi host with everything present.</summary>
    private static readonly TierGates _amd = _intelIhd with { Driver = VaapiDriver.Amd };

    /// <summary>Gate combinations by name, each with the tier upstream's dispatch selects.</summary>
    private static readonly Dictionary<string, (TierGates Gates, PipelineTier Expected)> _cases = new()
    {
        ["VAAPI Intel iHD"] = (_intelIhd, PipelineTier.FullOpencl),
        ["VAAPI AMD"] = (_amd, PipelineTier.FullVulkan),
        ["VAAPI AMD without Vulkan filters"] = (_amd with { VulkanFull = false }, PipelineTier.Limited),
        ["VAAPI AMD without DRM interop"] = (_amd with { VulkanDrmInterop = false }, PipelineTier.Limited),
        ["VAAPI AMD on an old kernel"] = (_amd with { KernelVersion = _oldKernel }, PipelineTier.Limited),
        ["VAAPI i965"] = (_intelIhd with { Driver = VaapiDriver.IntelI965 }, PipelineTier.Limited),
        ["VAAPI other driver"] = (_intelIhd with { Driver = VaapiDriver.Other }, PipelineTier.Limited),
        ["VAAPI without alphasrc"] = (_intelIhd with { Alphasrc = false }, PipelineTier.LegacyCopyBack),
        ["VAAPI AMD without OpenCL"] = (_amd with { OpenclFull = false }, PipelineTier.LegacyCopyBack),
        ["VAAPI on Windows"] = (_intelIhd with { Platform = HostOs.Windows }, PipelineTier.LegacyCopyBack),
        ["VAAPI without hardware codec"] = (_intelIhd with { HasHardwareCodec = false }, PipelineTier.LegacyCopyBack),
        ["VAAPI MPEG-4 input"] = (_intelIhd with { InputIsMpeg4 = true }, PipelineTier.LegacyCopyBack),
        ["VAAPI without VAAPI hwaccel"] = (_intelIhd with { HwaccelVaapi = false }, PipelineTier.LegacyCopyBack),
        ["VAAPI without VAAPI filters"] = (_intelIhd with { VaapiFull = false }, PipelineTier.LegacyCopyBack),
        ["VAAPI without OpenCL"] = (_intelIhd with { OpenclFull = false }, PipelineTier.LegacyCopyBack),
        ["QSV Linux without VAAPI filters or driver match"] = (_intelIhd with { Backend = HwType.qsv, VaapiFull = false, Driver = VaapiDriver.Other }, PipelineTier.FullOpencl),
        ["QSV Windows over D3D11"] = (_intelIhd with { Backend = HwType.qsv, Platform = HostOs.Windows, HwaccelVaapi = false, HwaccelD3d11va = true }, PipelineTier.FullOpencl),
        ["QSV without alphasrc"] = (_intelIhd with { Backend = HwType.qsv, Alphasrc = false }, PipelineTier.LegacyCopyBack),
        ["QSV without QSV hwaccel"] = (_intelIhd with { Backend = HwType.qsv, HwaccelQsv = false }, PipelineTier.LegacyCopyBack),
        ["QSV without OpenCL"] = (_intelIhd with { Backend = HwType.qsv, OpenclFull = false }, PipelineTier.LegacyCopyBack),
        ["QSV Linux without VAAPI hwaccel"] = (_intelIhd with { Backend = HwType.qsv, HwaccelVaapi = false }, PipelineTier.LegacyCopyBack),
        ["QSV Windows without D3D11"] = (_intelIhd with { Backend = HwType.qsv, Platform = HostOs.Windows }, PipelineTier.LegacyCopyBack),
        ["QSV other OS"] = (_intelIhd with { Backend = HwType.qsv, Platform = HostOs.Other, HwaccelD3d11va = true }, PipelineTier.LegacyCopyBack),
        ["QSV without hardware codec"] = (_intelIhd with { Backend = HwType.qsv, HasHardwareCodec = false }, PipelineTier.LegacyCopyBack),
        ["none"] = (_intelIhd with { Backend = HwType.none }, PipelineTier.Unknown),
        ["amf"] = (_intelIhd with { Backend = HwType.amf }, PipelineTier.Unknown),
        ["nvenc"] = (_intelIhd with { Backend = HwType.nvenc }, PipelineTier.Unknown),
        ["v4l2m2m"] = (_intelIhd with { Backend = HwType.v4l2m2m }, PipelineTier.Unknown),
        ["videotoolbox"] = (_intelIhd with { Backend = HwType.videotoolbox }, PipelineTier.Unknown),
        ["rkmpp"] = (_intelIhd with { Backend = HwType.rkmpp }, PipelineTier.Unknown),
    };

    /// <summary>Gets the names of the gate combinations.</summary>
    public static TheoryData<string> CaseNames => [.. _cases.Keys];

    /// <summary>Every combination of inputs resolves without throwing, and never to Unknown.</summary>
    [Fact]
    public void TotalOverAllInputs()
    {
        var count = 0;
        foreach (var gates in AllCombinations())
        {
            Assert.NotEqual(PipelineTier.Unknown, TierResolver.Resolve(gates));
            count++;
        }

        Assert.Equal(2 * Enum.GetValues<HostOs>().Length * Enum.GetValues<VaapiDriver>().Length * 2 * (1 << 10), count);
    }

    /// <summary>Each gate combination resolves to upstream's tier.</summary>
    /// <param name="name">The combination.</param>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ResolvesTier(string name) =>
        Assert.Equal(_cases[name].Expected, TierResolver.Resolve(_cases[name].Gates));

    /// <summary>The composite gates require every upstream component.</summary>
    [Fact]
    public void BuildGatesRequireEveryComponent()
    {
        string[] vaapi = ["drm", "vaapi", "scale_vaapi", "deinterlace_vaapi", "tonemap_vaapi", "procamp_vaapi", "OverlayVaapiFrameSync", "transpose_vaapi", "hwupload_vaapi"];
        string[] opencl = ["opencl", "scale_opencl", "TonemapOpenclBt2390", "OverlayOpenclFrameSync"];
        string[] vulkan = ["vulkan", "libplacebo", "scale_vulkan", "OverlayVulkanFrameSync", "transpose_vulkan", "flip_vulkan"];

        AssertRequiresAll(vaapi, BuildGates.VaapiFull);
        AssertRequiresAll(opencl, BuildGates.OpenclFull);
        AssertRequiresAll(vulkan, BuildGates.VulkanFull);
    }

    /// <summary>Asserts a gate passes with every name present and fails when any one is removed.</summary>
    /// <param name="names">Every hwaccel, filter and filter-option name the gate needs.</param>
    /// <param name="gate">The gate under test.</param>
    private static void AssertRequiresAll(string[] names, Func<Func<string, bool>, Func<string, bool>, Func<string, bool>, bool> gate)
    {
        Assert.True(gate(names.Contains, names.Contains, names.Contains));
        foreach (var missing in names)
        {
            var present = names.Where(n => n != missing).ToHashSet();
            Assert.False(gate(present.Contains, present.Contains, present.Contains), $"passed without {missing}");
        }
    }

    /// <summary>Enumerates every VAAPI/QSV input combination.</summary>
    /// <returns>All combinations.</returns>
    private static IEnumerable<TierGates> AllCombinations()
    {
        foreach (var backend in (HwType[])[HwType.vaapi, HwType.qsv])
        {
            foreach (var platform in Enum.GetValues<HostOs>())
            {
                foreach (var driver in Enum.GetValues<VaapiDriver>())
                {
                    foreach (var kernel in (Version[])[_oldKernel, _newKernel])
                    {
                        for (var bits = 0; bits < 1 << 10; bits++)
                        {
                            yield return new TierGates(
                                backend,
                                platform,
                                HasHardwareCodec: (bits & 1) != 0,
                                InputIsMpeg4: (bits & 2) != 0,
                                HwaccelVaapi: (bits & 4) != 0,
                                HwaccelQsv: (bits & 8) != 0,
                                HwaccelD3d11va: (bits & 16) != 0,
                                VaapiFull: (bits & 32) != 0,
                                OpenclFull: (bits & 64) != 0,
                                VulkanFull: (bits & 128) != 0,
                                Alphasrc: (bits & 256) != 0,
                                driver,
                                VulkanDrmInterop: (bits & 512) != 0,
                                kernel);
                        }
                    }
                }
            }
        }
    }
}
