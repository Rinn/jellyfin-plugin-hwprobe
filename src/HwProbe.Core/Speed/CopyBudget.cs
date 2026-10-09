using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Resources;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Measures what one copy needs and keeps runs of copies within the memory and CPU the server has to spare.</summary>
/// <remarks>
/// One copy's memory is the drop in free memory while it runs, so it covers GPU surfaces no process is charged for. Its CPU is
/// its own CPU time when resources were measured, else the server's busy time over the run. Copies run flat out, so the CPU one
/// needs to keep real time is its share at one copy's speed. A single copy is never stopped: it's what one transcode takes.
/// </remarks>
public sealed class CopyBudget : ICopyBudget
{
    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(200);

    private readonly Func<MemorySnapshot?> _readMemory;
    private readonly Func<double?> _readBusy;
    private readonly int _processors;
    private readonly CatalogConcurrency _limits;
    private readonly TimeProvider _time;
    private readonly TimeSpan _sampleEvery;
    private long? _copyBytes;
    private double? _copyCores;

    /// <summary>Initializes a new instance of the <see cref="CopyBudget"/> class.</summary>
    /// <param name="readMemory">Reads the memory now; null where it can't be read, which leaves the count unlimited by memory.</param>
    /// <param name="readBusy">Reads the server's busy CPU seconds; null where it can't be read.</param>
    /// <param name="processors">The cores the server may use, which .NET reports within a container's CPU limit.</param>
    /// <param name="limits">The reserves and margins.</param>
    /// <param name="time">Paces sampling.</param>
    public CopyBudget(Func<MemorySnapshot?> readMemory, Func<double?> readBusy, int processors, CatalogConcurrency limits, TimeProvider time)
        : this(readMemory, readBusy, processors, limits, time, _interval)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CopyBudget"/> class that samples memory at a given interval.</summary>
    /// <param name="readMemory">Reads the memory now.</param>
    /// <param name="readBusy">Reads the server's busy CPU seconds.</param>
    /// <param name="processors">The cores the server may use.</param>
    /// <param name="limits">The reserves and margins.</param>
    /// <param name="time">Paces sampling.</param>
    /// <param name="sampleEvery">How often memory is read while copies run.</param>
    internal CopyBudget(Func<MemorySnapshot?> readMemory, Func<double?> readBusy, int processors, CatalogConcurrency limits, TimeProvider time, TimeSpan sampleEvery)
    {
        _readMemory = readMemory;
        _readBusy = readBusy;
        _processors = processors;
        _limits = limits;
        _time = time;
        _sampleEvery = sampleEvery;
    }

    /// <summary>Gets the stand-in for a copy stopped because memory ran low.</summary>
    public static FfmpegRunResult Stopped { get; } = new(FfmpegRunStatus.TimedOut, null, string.Empty, string.Empty, null, TimeSpan.Zero, null);

    /// <inheritdoc/>
    public bool LastStopped { get; private set; }

    /// <inheritdoc/>
    public CopyLimit? MostCopies(double speed)
    {
        int? byMemory = _copyBytes is { } copy && _readMemory() is { } now
            ? Floor((now.Available - Reserve(now)) / (copy * _limits.MemoryMargin))
            : null;
        int? byCpu = _copyCores is > 0 and var cores && speed > 0
            ? Floor(_processors * _limits.CpuShare / (cores / speed))
            : null;
        return (byMemory, byCpu) switch
        {
            ({ } memory, { } cpu) => new CopyLimit(Math.Min(memory, cpu), cpu < memory),
            ({ } memory, null) => new CopyLimit(memory, false),
            (null, { } cpu) => new CopyLimit(cpu, true),
            _ => null,
        };
    }

    /// <summary>Runs copies together under the memory guard and records what they took.</summary>
    /// <param name="copies">How many to run at once.</param>
    /// <param name="run">Runs one copy until it ends or its token is cancelled.</param>
    /// <param name="cancellationToken">The run's own cancellation, which still throws.</param>
    /// <returns>Each copy's result, or <see cref="Stopped"/> for each when memory ran low.</returns>
    public async Task<IReadOnlyList<FfmpegRunResult>> RunAsync(int copies, Func<CancellationToken, Task<FfmpegRunResult>> run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var watch = new CopyWatch(_readMemory, _readBusy, copies > 1 ? now => (long)(Reserve(now) * _limits.MemoryStopShare) : _ => long.MinValue, _time, _sampleEvery, cancellationToken);
        IReadOnlyList<FfmpegRunResult> runs;
        try
        {
            runs = await Task.WhenAll(Enumerable.Range(0, copies).Select(_ => run(watch.Token)));
        }
        catch (OperationCanceledException) when (watch.Tripped && !cancellationToken.IsCancellationRequested)
        {
            // The runner killed every copy's process tree before rethrowing.
            runs = [.. Enumerable.Repeat(Stopped, copies)];
        }
        finally
        {
            await watch.DisposeAsync();
        }

        Finish(watch, runs);
        return runs;
    }

    /// <summary>Rounds a share of copies down, to at least one: one copy has already run.</summary>
    /// <param name="copies">The copies there's room for.</param>
    /// <returns>The whole number.</returns>
    private static int Floor(double copies) => (int)Math.Clamp(Math.Floor(copies), 1, int.MaxValue);

    /// <summary>Records a finished run: whether memory stopped it, and the memory and CPU a copy took.</summary>
    /// <param name="watch">The run's watch, disposed.</param>
    /// <param name="runs">The copies' results.</param>
    private void Finish(CopyWatch watch, IReadOnlyList<FfmpegRunResult> runs)
    {
        LastStopped = watch.Tripped;
        if (watch.Before is { } before && watch.Lowest is { } lowest)
        {
            // A run that ran out took more each than one copy did alone, which the next cap should know.
            _copyBytes = Math.Max(_copyBytes ?? _limits.MemoryMinimumCopyMiB * 1024L * 1024L, (before.Available - lowest) / runs.Count);
        }

        if (runs.Count == 1 && !watch.Tripped)
        {
            var cores = runs[0].Resources is { CpuSeconds: { } cpu, Seconds: > 0 and var seconds } ? cpu / seconds : watch.BusyCores;
            if (cores is { } measured)
            {
                _copyCores = Math.Max(_copyCores ?? 0, measured);
            }
        }
    }

    /// <summary>Returns the memory kept free: a share of the server's, at least the minimum, and at most half of it.</summary>
    /// <param name="now">A reading.</param>
    /// <returns>The reserve in bytes.</returns>
    private long Reserve(MemorySnapshot now) =>
        Math.Min(Math.Max(_limits.MemoryReserveMinimumMiB * 1024L * 1024L, (long)(now.Total * _limits.MemoryReserveShare)), now.Total / 2);
}
