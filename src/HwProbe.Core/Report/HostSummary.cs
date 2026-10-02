namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>The host the probe ran on.</summary>
/// <param name="Os">OS platform, lowercase.</param>
/// <param name="Kernel">Kernel or OS version.</param>
/// <param name="Container">Container runtime, or null on bare metal.</param>
public sealed record HostSummary(string Os, string Kernel, string? Container)
{
    /// <summary>Gets the GPU vendor IDs, e.g. <c>0x8086</c>: on Linux every PCI display controller, driver loaded or not; on Windows the Direct3D adapters the probe opened. Empty on macOS, for non-PCI GPUs and in a VM with a virtual GPU.</summary>
    public IReadOnlyList<string> GpuVendors { get; init; } = [];

    /// <summary>Gets the CPU architecture, e.g. <c>x64</c> or <c>arm64</c>; <c>unknown</c> in reports from before it was recorded.</summary>
    public string Architecture { get; init; } = "unknown";
}
