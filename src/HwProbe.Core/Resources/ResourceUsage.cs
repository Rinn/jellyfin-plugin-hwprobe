namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>What one ffmpeg run used, as far as the platform reports it.</summary>
/// <param name="Seconds">Wall-clock time the figures cover.</param>
/// <param name="CpuSeconds">CPU time across all cores, or null when unavailable.</param>
/// <param name="PeakMemoryBytes">The most memory held at once, or null when unavailable.</param>
public sealed record ResourceUsage(double Seconds, double? CpuSeconds, long? PeakMemoryBytes)
{
    /// <summary>Gets the seconds each GPU engine was busy, by the engine's name as the platform gives it (e.g. <c>VideoDecode</c>, <c>video</c>); null when unavailable.</summary>
    public IReadOnlyDictionary<string, double>? GpuSeconds { get; init; }

    /// <summary>Gets a value indicating whether <see cref="GpuSeconds"/> covers the whole GPU rather than this process alone (NVIDIA on Linux).</summary>
    public bool GpuWholeDevice { get; init; }

    /// <summary>Gets the most GPU memory held at once (dedicated memory on Windows, resident GPU buffers on Linux), or null when unavailable.</summary>
    public long? PeakGpuMemoryBytes { get; init; }

    /// <summary>Gets the energy each whole device used over the run, in joules, by domain (<c>Cpu</c> for the CPU package, <c>Gpu</c>); null when no meter is readable.</summary>
    /// <remarks>Whole-device figures: other work on the host counts too, so <see cref="IdleWatts"/> is read just before the run.</remarks>
    public IReadOnlyDictionary<string, double>? Joules { get; init; }

    /// <summary>Gets each domain's power just before the run, in watts, to subtract from <see cref="Joules"/>.</summary>
    public IReadOnlyDictionary<string, double>? IdleWatts { get; init; }

    /// <summary>Returns each domain's average power over the run above its idle reading, in watts.</summary>
    /// <returns>Watts by domain, or empty without energy figures.</returns>
    public IReadOnlyDictionary<string, double> WattsAboveIdle() =>
        Joules is null || Seconds <= 0 ? new Dictionary<string, double>(StringComparer.Ordinal)
        : Joules.ToDictionary(j => j.Key, j => Math.Max(0, (j.Value / Seconds) - (IdleWatts?.GetValueOrDefault(j.Key) ?? 0)), StringComparer.Ordinal);
}
