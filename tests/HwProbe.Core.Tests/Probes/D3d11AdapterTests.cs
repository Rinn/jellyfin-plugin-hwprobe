using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Reading Direct3D adapters from D3D11VA device opens.</summary>
[Trait("Category", "Unit")]
public sealed class D3d11AdapterTests
{
    /// <summary>The vendor, device, and name come from ffmpeg's "Using device" line, logged even when the create then fails.</summary>
    /// <param name="stderr">The device open's stderr.</param>
    /// <param name="vendor">The expected vendor, or null when no adapter is named.</param>
    /// <param name="device">The expected device ID.</param>
    /// <param name="name">The expected adapter name.</param>
    [Theory]
    [InlineData("[D3D11VA @ 000001ed8e10de40] Selecting d3d11va adapter 0\n[D3D11VA @ 000001ed8e10de40] Using device 4d4f4351:36334330 (Qualcomm(R) Adreno(TM) X1-85 GPU).\n", "0x4d4f4351", "0x36334330", "Qualcomm(R) Adreno(TM) X1-85 GPU")]
    [InlineData("[D3D11VA @ 0000020a] Using device 10de:2c02 (NVIDIA GeForce RTX 5080).\r\n", "0x10de", "0x2c02", "NVIDIA GeForce RTX 5080")]
    [InlineData("[D3D11VA @ 0000020a] Using device 1414:008c (Microsoft Basic Render Driver).\n[D3D11VA @ 0000020a] Failed to create Direct3D device (887a0004)\n", "0x1414", "0x008c", "Microsoft Basic Render Driver")]
    [InlineData("[D3D11VA @ 0000020a] Selecting d3d11va adapter 4\n", null, null, null)]
    public void ParsesTheAdapter(string stderr, string? vendor, string? device, string? name) =>
        Assert.Equal(vendor is null || device is null || name is null ? null : (vendor, device, name), D3d11Adapter.Parse(stderr));
}
