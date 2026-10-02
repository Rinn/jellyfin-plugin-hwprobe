namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>GPU vendors that mean a VM or a software adapter, where a vendor list can't say which real GPUs exist.</summary>
public static class VirtualGpus
{
    /// <summary>Gets Microsoft (WSL2, Hyper-V, Windows' software and driverless adapters), virtio, Red Hat, VMware, VirtualBox and QEMU VGA.</summary>
    /// <remarks>WSL2 shows a 0x1414 3D controller while the real GPU arrives through /dev/dxg (seen in Docker Desktop with an RTX 5080).</remarks>
    public static IReadOnlyList<string> Vendors { get; } = ["0x1414", "0x1af4", "0x1b36", "0x15ad", "0x80ee", "0x1234"];
}
