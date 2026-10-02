using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Reading the adapter vendor from a D3D11VA device open.</summary>
[Trait("Category", "Unit")]
public sealed class D3d11AdapterTests
{
    /// <summary>The vendor comes from ffmpeg's "Using device" line; a log without one has none.</summary>
    /// <param name="stderr">The device open's stderr.</param>
    /// <param name="vendor">The expected vendor, or null.</param>
    [Theory]
    [InlineData("[D3D11VA @ 000001ed8e10de40] Using device 4d4f4351:36334330 (Qualcomm(R) Adreno(TM) X1-85 GPU).\n", "0x4d4f4351")]
    [InlineData("[D3D11VA @ 0000020a] Selecting d3d11va adapter 0\n[D3D11VA @ 0000020a] Using device 10de:2c02 (NVIDIA GeForce RTX 5080).\n", "0x10de")]
    [InlineData("[D3D11VA @ 0000020a] Failed to create Direct3D device (887a0004)\n", null)]
    public void ReadsTheVendor(string stderr, string? vendor) =>
        Assert.Equal(vendor, D3d11Adapter.Vendor(stderr));
}
