namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>Host facts for the report and fingerprint.</summary>
/// <param name="Os">The OS family.</param>
/// <param name="Kernel">Kernel release (Linux, macOS) or OS version (Windows), or <c>unknown</c>.</param>
/// <param name="Container">Detected container runtime, e.g. <c>docker</c>, or null on bare metal.</param>
public sealed record HostInfo(HostOs Os, string Kernel, string? Container);
