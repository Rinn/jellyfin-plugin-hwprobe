namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>PCI identity of a Linux render node, for the fingerprint.</summary>
/// <param name="Node">The render node path, e.g. <c>/dev/dri/renderD128</c>.</param>
/// <param name="Vendor">PCI vendor ID from sysfs, or <c>unknown</c>.</param>
/// <param name="Device">PCI device ID from sysfs, or <c>unknown</c>.</param>
public sealed record RenderNodeIdentity(string Node, string Vendor, string Device);
