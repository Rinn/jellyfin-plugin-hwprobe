namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Where a running probe is.</summary>
/// <param name="Step">What it's doing, e.g. <c>Testing vaapi: hevc-10bit decode</c>.</param>
/// <param name="Done">Tests finished.</param>
/// <param name="Total">Tests planned, or 0 before the devices are known.</param>
public sealed record ProbeProgress(string Step, int Done, int Total);
