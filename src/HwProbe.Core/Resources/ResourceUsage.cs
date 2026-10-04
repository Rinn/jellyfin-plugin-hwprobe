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
}
