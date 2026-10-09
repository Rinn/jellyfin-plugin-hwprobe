using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>The probed device's traits replace the server's; everything else is forwarded.</summary>
[Trait("Category", "Unit")]
public sealed class TraitMediaEncoderTests
{
    /// <summary>Driver traits come from the probe, not the wrapped encoder.</summary>
    [Fact]
    public void TraitsOverrideInnerEncoder()
    {
        var inner = ProbeMediaEncoder.Create(TestCapabilities.Full with { IsVaapiDeviceInteliHD = true }, new CallRecorder());

        var wrapped = TraitMediaEncoder.Create(inner, new DeviceTraits(VaapiDriver.Amd));

        Assert.True(wrapped.IsVaapiDeviceAmd);
        Assert.False(wrapped.IsVaapiDeviceInteliHD);
        Assert.False(wrapped.IsVaapiDeviceInteli965);
    }

    /// <summary>The server's Vulkan DRM support describes its own device, so an AMD probe takes it only when the server's device is AMD too.</summary>
    /// <param name="serverAmd">Whether the server's configured device is AMD.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VulkanSupportCarriesOverOnlyFromAnAmdServerDevice(bool serverAmd)
    {
        var caps = TestCapabilities.Full with { IsVaapiDeviceAmd = serverAmd, IsVaapiDeviceInteliHD = !serverAmd, IsVaapiDeviceSupportVulkanDrmInterop = true, IsVaapiDeviceSupportVulkanDrmModifier = true };
        var inner = ProbeMediaEncoder.Create(caps, new CallRecorder());

        var amd = TraitMediaEncoder.Create(inner, new DeviceTraits(VaapiDriver.Amd));
        var intel = TraitMediaEncoder.Create(inner, new DeviceTraits(VaapiDriver.IntelIhd));

        Assert.Equal((serverAmd, serverAmd), (amd.IsVaapiDeviceSupportVulkanDrmInterop, amd.IsVaapiDeviceSupportVulkanDrmModifier));
        Assert.False(intel.IsVaapiDeviceSupportVulkanDrmInterop);
    }

    /// <summary>Other members reach the wrapped encoder unchanged.</summary>
    [Fact]
    public void OtherMembersForward()
    {
        var recorder = new CallRecorder();
        var inner = ProbeMediaEncoder.Create(TestCapabilities.Full, recorder);

        var wrapped = TraitMediaEncoder.Create(inner, new DeviceTraits(VaapiDriver.IntelIhd));

        Assert.True(wrapped.SupportsEncoder("h264_videotoolbox"));
        Assert.Contains("IMediaEncoder.SupportsEncoder", recorder.Calls);
        Assert.Equal(TestCapabilities.Full.EncoderPath, wrapped.EncoderPath);
    }
}
