using Jellyfin.Plugin.HwProbe.Core.Resources;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Compares what two results used, to prefer the more efficient of two that measured alike.</summary>
public static class ResourceComparison
{
    // Below these, a difference is sampling noise rather than a saving.
    private const double Margin = 0.10;
    private const double MinCores = 0.05;
    private const double MinGpuShare = 0.02;
    private const long MinBytes = 16L * 1024 * 1024;

    /// <summary>Returns the resources one result used notably less of than another, when it used notably more of none.</summary>
    /// <param name="a">The result that may be more efficient.</param>
    /// <param name="b">The result it's compared with.</param>
    /// <returns>The savings, largest first; empty when either wasn't measured, nothing differs enough, or <paramref name="a"/> costs more of something.</returns>
    /// <remarks>GPU figures are compared only on the same backend and device: engines and memory on different GPUs aren't alike.</remarks>
    public static IReadOnlyList<ResourceSaving> Savings(SpeedResult a, SpeedResult b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Resources is not { Seconds: > 0 } ra || b.Resources is not { Seconds: > 0 } rb)
        {
            return [];
        }

        var sameGpu = a.Type == b.Type && a.Device == b.Device && ra.GpuWholeDevice == rb.GpuWholeDevice;
        List<(string Resource, double? A, double? B, double Floor)> figures =
        [
            ("Cpu", ra.CpuSeconds / ra.Seconds, rb.CpuSeconds / rb.Seconds, MinCores),
            ("Memory", ra.PeakMemoryBytes, rb.PeakMemoryBytes, MinBytes),
        ];
        if (sameGpu)
        {
            figures.Add(("Gpu", Busiest(ra), Busiest(rb), MinGpuShare));
            figures.Add(("GpuMemory", ra.PeakGpuMemoryBytes, rb.PeakGpuMemoryBytes, MinBytes));
        }

        List<ResourceSaving> savings = [];
        foreach (var (resource, x, y, floor) in figures)
        {
            if (x is not { } mine || y is not { } theirs || theirs <= 0)
            {
                continue;
            }

            if (mine > theirs * (1 + Margin) && mine - theirs > floor)
            {
                return [];
            }

            if (mine < theirs * (1 - Margin) && theirs - mine > floor)
            {
                savings.Add(new ResourceSaving(resource, 1 - (mine / theirs)));
            }
        }

        return [.. savings.OrderByDescending(s => s.Fraction)];
    }

    /// <summary>Returns the busiest GPU engine's share of the run.</summary>
    /// <param name="usage">The usage.</param>
    /// <returns>From 0 to 1, or null without GPU figures.</returns>
    private static double? Busiest(ResourceUsage usage) =>
        usage.GpuSeconds is { Count: > 0 } engines ? engines.Values.Max() / usage.Seconds : null;
}
