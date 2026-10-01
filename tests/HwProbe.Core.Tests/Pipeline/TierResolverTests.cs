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

        Assert.Equal(2 * Enum.GetValues<HostOs>().Length * 4 * 2 * (1 << 10), count);
    }

    /// <summary>Intel iHD with OpenCL and alphasrc gets the full OpenCL pipeline.</summary>
    [Fact]
    public void VaapiIntelIhdIsFullOpencl() => Assert.Equal(PipelineTier.FullOpencl, TierResolver.Resolve(_intelIhd));

    /// <summary>AMD with Vulkan, DRM interop and a new enough kernel gets the Vulkan pipeline.</summary>
    [Fact]
    public void VaapiAmdIsFullVulkan() => Assert.Equal(PipelineTier.FullVulkan, TierResolver.Resolve(_amd));

    /// <summary>AMD missing any Vulkan prerequisite drops to Limited.</summary>
    /// <param name="vulkanFull">Vulkan filters present.</param>
    /// <param name="interop">DRM interop supported.</param>
    /// <param name="newKernel">Kernel at least 5.15.</param>
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void VaapiAmdWithoutVulkanIsLimited(bool vulkanFull, bool interop, bool newKernel)
    {
        var gates = _amd with { VulkanFull = vulkanFull, VulkanDrmInterop = interop, KernelVersion = newKernel ? _newKernel : _oldKernel };

        Assert.Equal(PipelineTier.Limited, TierResolver.Resolve(gates));
    }

    /// <summary>i965 and unrecognised drivers get Limited.</summary>
    /// <param name="driver">The VA-API driver.</param>
    [Theory]
    [InlineData(VaapiDriver.IntelI965)]
    [InlineData(VaapiDriver.Other)]
    public void VaapiOtherDriversAreLimited(VaapiDriver driver) =>
        Assert.Equal(PipelineTier.Limited, TierResolver.Resolve(_intelIhd with { Driver = driver }));

    /// <summary>OpenCL present but alphasrc missing falls back to copy-back.</summary>
    /// <param name="backend">VAAPI or QSV.</param>
    [Theory]
    [InlineData(HwType.vaapi)]
    [InlineData(HwType.qsv)]
    public void MissingAlphasrcIsLegacyCopyBack(HwType backend) =>
        Assert.Equal(PipelineTier.LegacyCopyBack, TierResolver.Resolve(_intelIhd with { Backend = backend, Alphasrc = false }));

    /// <summary>Without OpenCL even an AMD host with full Vulkan falls back to copy-back.</summary>
    [Fact]
    public void VaapiAmdWithoutOpenclIsLegacyCopyBack() =>
        Assert.Equal(PipelineTier.LegacyCopyBack, TierResolver.Resolve(_amd with { OpenclFull = false }));

    /// <summary>Each VAAPI prerequisite for any preferred path, when absent, gives copy-back.</summary>
    /// <param name="change">Which prerequisite to remove.</param>
    [Theory]
    [InlineData("platform")]
    [InlineData("codec")]
    [InlineData("mpeg4")]
    [InlineData("hwaccel")]
    [InlineData("vaapiFull")]
    [InlineData("opencl")]
    public void VaapiPrerequisitesGateCopyBack(string change)
    {
        var gates = change switch
        {
            "platform" => _intelIhd with { Platform = HostOs.Windows },
            "codec" => _intelIhd with { HasHardwareCodec = false },
            "mpeg4" => _intelIhd with { InputIsMpeg4 = true },
            "hwaccel" => _intelIhd with { HwaccelVaapi = false },
            "vaapiFull" => _intelIhd with { VaapiFull = false },
            _ => _intelIhd with { OpenclFull = false },
        };

        Assert.Equal(PipelineTier.LegacyCopyBack, TierResolver.Resolve(gates));
    }

    /// <summary>QSV over VAAPI on Linux is full OpenCL and needs no VAAPI-full filters or driver match.</summary>
    [Fact]
    public void QsvLinuxIsFullOpenclWithoutVaapiFull()
    {
        var gates = _intelIhd with { Backend = HwType.qsv, VaapiFull = false, Driver = VaapiDriver.Other };

        Assert.Equal(PipelineTier.FullOpencl, TierResolver.Resolve(gates));
    }

    /// <summary>QSV over D3D11 on Windows is full OpenCL.</summary>
    [Fact]
    public void QsvWindowsIsFullOpencl()
    {
        var gates = _intelIhd with { Backend = HwType.qsv, Platform = HostOs.Windows, HwaccelVaapi = false, HwaccelD3d11va = true };

        Assert.Equal(PipelineTier.FullOpencl, TierResolver.Resolve(gates));
    }

    /// <summary>Each QSV prerequisite, when absent, gives copy-back.</summary>
    /// <param name="change">Which prerequisite to remove.</param>
    [Theory]
    [InlineData("hwaccelQsv")]
    [InlineData("opencl")]
    [InlineData("parentVaapi")]
    [InlineData("windowsNoD3d11")]
    [InlineData("otherOs")]
    [InlineData("codec")]
    public void QsvPrerequisitesGateCopyBack(string change)
    {
        var qsv = _intelIhd with { Backend = HwType.qsv };
        var gates = change switch
        {
            "hwaccelQsv" => qsv with { HwaccelQsv = false },
            "opencl" => qsv with { OpenclFull = false },
            "parentVaapi" => qsv with { HwaccelVaapi = false },
            "windowsNoD3d11" => qsv with { Platform = HostOs.Windows },
            "otherOs" => qsv with { Platform = HostOs.Other, HwaccelD3d11va = true },
            _ => qsv with { HasHardwareCodec = false },
        };

        Assert.Equal(PipelineTier.LegacyCopyBack, TierResolver.Resolve(gates));
    }

    /// <summary>Backends without a tier dispatch report Unknown.</summary>
    /// <param name="backend">A backend other than VAAPI and QSV.</param>
    [Theory]
    [InlineData(HwType.none)]
    [InlineData(HwType.amf)]
    [InlineData(HwType.nvenc)]
    [InlineData(HwType.v4l2m2m)]
    [InlineData(HwType.videotoolbox)]
    [InlineData(HwType.rkmpp)]
    public void OtherBackendsAreUnknown(HwType backend) =>
        Assert.Equal(PipelineTier.Unknown, TierResolver.Resolve(_intelIhd with { Backend = backend }));

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
