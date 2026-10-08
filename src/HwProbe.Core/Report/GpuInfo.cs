namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>A GPU the probe saw, as the system names it.</summary>
/// <param name="Device">Its render node on Linux, e.g. <c>/dev/dri/renderD128</c>, or its Direct3D adapter on Windows, e.g. <c>dx11:0</c>.</param>
/// <param name="Vendor">The PCI vendor ID, e.g. <c>0x8086</c>.</param>
/// <param name="Id">The PCI device ID, e.g. <c>0x5a85</c>.</param>
/// <param name="Name">The adapter's name on Windows, or the VA-API driver line on Linux; null when the device didn't open far enough to give one.</param>
public sealed record GpuInfo(string Device, string Vendor, string Id, string? Name);
