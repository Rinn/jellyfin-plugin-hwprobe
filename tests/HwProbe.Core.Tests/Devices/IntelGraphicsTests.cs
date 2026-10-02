using Jellyfin.Plugin.HwProbe.Core.Devices;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Devices;

/// <summary>Classifying Intel GPUs from sysfs PCI IDs by the low-power encoders they have.</summary>
[Trait("Category", "Unit")]
public sealed class IntelGraphicsTests
{
    /// <summary>Gen 8 and older have none, Gen 9.x has H.264 only, and newer Intel, other vendors and unreadable IDs are unknown.</summary>
    /// <param name="vendor">The PCI vendor ID.</param>
    /// <param name="device">The PCI device ID.</param>
    /// <param name="expected">The expected support.</param>
    [Theory]
    [InlineData("0x8086", "0x1616", LowPowerSupport.None)]
    [InlineData("0x8086", "0x0412", LowPowerSupport.None)]
    [InlineData("0x8086", "0x22b0", LowPowerSupport.None)]
    [InlineData("0x8086", "0x5a85", LowPowerSupport.H264Only)]
    [InlineData("0x8086", "0x3185", LowPowerSupport.H264Only)]
    [InlineData("0x8086", "0x3e92", LowPowerSupport.H264Only)]
    [InlineData("0x8086", "0x9bc5", LowPowerSupport.H264Only)]
    [InlineData("0x8086", "0x8a52", LowPowerSupport.Unknown)]
    [InlineData("0x8086", "0x46a6", LowPowerSupport.Unknown)]
    [InlineData("0x10de", "0x5a85", LowPowerSupport.Unknown)]
    [InlineData("0x8086", "unknown", LowPowerSupport.Unknown)]
    public void ClassifiesByGeneration(string vendor, string device, LowPowerSupport expected) =>
        Assert.Equal(expected, IntelGraphics.LowPower(vendor, device));
}
