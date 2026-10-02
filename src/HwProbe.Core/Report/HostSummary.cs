namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>The host the probe ran on.</summary>
/// <param name="Os">OS platform, lowercase.</param>
/// <param name="Kernel">Kernel or OS version.</param>
/// <param name="Container">Container runtime, or null on bare metal.</param>
public sealed record HostSummary(string Os, string Kernel, string? Container)
{
    /// <summary>Gets the PCI vendor IDs of the host's display controllers, e.g. <c>0x8086</c>, driver loaded or not; empty where none could be read (Windows, macOS, non-PCI GPUs).</summary>
    public IReadOnlyList<string> GpuVendors { get; init; } = [];

    /// <summary>Gets the CPU architecture, e.g. <c>x64</c> or <c>arm64</c>; <c>unknown</c> in reports from before it was recorded.</summary>
    public string Architecture { get; init; } = "unknown";
}
