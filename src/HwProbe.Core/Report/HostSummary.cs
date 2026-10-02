namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>The host the probe ran on.</summary>
/// <param name="Os">OS platform, lowercase.</param>
/// <param name="Kernel">Kernel or OS version.</param>
/// <param name="Container">Container runtime, or null on bare metal.</param>
public sealed record HostSummary(string Os, string Kernel, string? Container)
{
    /// <summary>Gets the PCI vendor IDs of the render nodes, e.g. <c>0x8086</c>; empty where none could be read (Windows, macOS, non-PCI GPUs). In a container, only the GPUs passed in.</summary>
    public IReadOnlyList<string> GpuVendors { get; init; } = [];
}
