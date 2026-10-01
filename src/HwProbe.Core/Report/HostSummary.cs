namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>The host the probe ran on.</summary>
/// <param name="Os">OS platform, lowercase.</param>
/// <param name="Kernel">Kernel or OS version.</param>
/// <param name="Container">Container runtime, or null on bare metal.</param>
public sealed record HostSummary(string Os, string Kernel, string? Container);
