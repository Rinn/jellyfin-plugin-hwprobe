using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Resources;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Measures what one copy needs and keeps runs of copies within the memory and CPU the server has to spare.</summary>
/// <remarks>
/// One copy's memory is the drop in free memory while it runs, so it covers GPU surfaces no process is charged for. Its CPU is
/// its own CPU time when resources were measured, else the host's busy time over the run, which also counts other load.
/// Copies run flat out, so the CPU one needs to keep real time is its share at one copy's speed.
/// </remarks>
/// <param name="readMemory">Reads the memory now; null where it can't be read, which leaves the count unlimited by memory.</param>
/// <param name="readBusy">Reads the host's busy CPU seconds; null where it can't be read.</param>
/// <param name="processors">The cores the server may use, which .NET reports within a container's CPU limit.</param>
/// <param name="limits">The reserves and margins.</param>
/// <param name="time">Paces sampling.</param>
public sealed class CopyBudget(Func<MemorySnapshot?> readMemory, Func<double?> readBusy, int processors, CatalogConcurrency limits, TimeProvider time) : ICopyBudget
{
    /// <summary>The least memory a copy counts as, so a run too short to sample still counts.</summary>
    public const long MinimumCopyBytes = 64L * 1024 * 1024;

    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(200);

    private long? _copyBytes;
    private double? _copyCores;

    /// <summary>Gets the stand-in for a copy stopped because memory ran low.</summary>
    public static FfmpegRunResult Stopped { get; } = new(FfmpegRunStatus.TimedOut, null, string.Empty, string.Empty, null, TimeSpan.Zero, null);

    /// <inheritdoc/>
    public bool LastStopped { get; private set; }

    /// <inheritdoc/>
    public CopyLimit? MostCopies(double speed)
    {
        int? byMemory = _copyBytes is { } copy && readMemory() is { } now
            ? Floor((now.Available - Reserve(now)) / (copy * limits.MemoryMargin))
            : null;
        int? byCpu = _copyCores is > 0 and var cores && speed > 0
            ? Floor(processors * limits.CpuShare / (cores / speed))
            : null;
        return (byMemory, byCpu) switch
        {
            ({ } memory, { } cpu) => new CopyLimit(Math.Min(memory, cpu), cpu < memory),
            ({ } memory, null) => new CopyLimit(memory, false),
            (null, { } cpu) => new CopyLimit(cpu, true),
            _ => null,
        };
    }

    /// <summary>Starts watching a run of copies.</summary>
    /// <param name="cancellationToken">The run's own cancellation.</param>
    /// <returns>The watch, whose token the copies use.</returns>
    public CopyWatch Watch(CancellationToken cancellationToken) =>
        new(readMemory, readBusy, now => (long)(Reserve(now) * limits.MemoryStopShare), time, _interval, cancellationToken);

    /// <summary>Records a finished run: whether memory stopped it, and for one copy, the memory and CPU it took.</summary>
    /// <param name="watch">The run's watch, disposed.</param>
    /// <param name="runs">The copies' results.</param>
    public void Finish(CopyWatch watch, IReadOnlyList<FfmpegRunResult> runs)
    {
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(runs);
        LastStopped = watch.Tripped;
        if (runs.Count != 1 || watch.Tripped)
        {
            return;
        }

        if (watch.Before is { } before && watch.Lowest is { } lowest)
        {
            _copyBytes = Math.Max(_copyBytes ?? MinimumCopyBytes, before.Available - lowest);
        }

        var cores = runs[0].Resources is { CpuSeconds: { } cpu, Seconds: > 0 and var seconds } ? cpu / seconds : watch.HostCores;
        if (cores is { } measured)
        {
            _copyCores = Math.Max(_copyCores ?? 0, measured);
        }
    }

    /// <summary>Rounds a share of copies down, to at least one: one copy has already run.</summary>
    /// <param name="copies">The copies there's room for.</param>
    /// <returns>The whole number.</returns>
    private static int Floor(double copies) => (int)Math.Clamp(Math.Floor(copies), 1, int.MaxValue);

    /// <summary>Returns the memory kept free.</summary>
    /// <param name="now">A reading.</param>
    /// <returns>The reserve in bytes.</returns>
    private long Reserve(MemorySnapshot now) => Math.Max(limits.MemoryReserveMinimumMiB * 1024L * 1024L, (long)(now.Total * limits.MemoryReserveShare));
}
