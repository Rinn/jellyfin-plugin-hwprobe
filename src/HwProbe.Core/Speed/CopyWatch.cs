using Jellyfin.Plugin.HwProbe.Core.Resources;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Watches a run of copies: keeps the least free memory, stops the run when too little is free, and times the host's busy CPU.</summary>
public sealed class CopyWatch : IAsyncDisposable
{
    private readonly Func<MemorySnapshot?> _readMemory;
    private readonly Func<double?> _readBusy;
    private readonly Func<MemorySnapshot, long> _stopUnder;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop;
    private readonly double? _busyBefore;
    private readonly long _started;

    // One sample at a time: timer callbacks can overlap when a read is slow.
    private readonly Lock _gate = new();
    private readonly ITimer _timer;

    /// <summary>Initializes a new instance of the <see cref="CopyWatch"/> class and starts sampling.</summary>
    /// <param name="readMemory">Reads the memory now.</param>
    /// <param name="readBusy">Reads the host's busy CPU seconds so far.</param>
    /// <param name="stopUnder">The free bytes under which the run is stopped, given a reading.</param>
    /// <param name="time">Paces sampling and times the run.</param>
    /// <param name="interval">How often memory is read.</param>
    /// <param name="cancellationToken">The run's own cancellation.</param>
    internal CopyWatch(Func<MemorySnapshot?> readMemory, Func<double?> readBusy, Func<MemorySnapshot, long> stopUnder, TimeProvider time, TimeSpan interval, CancellationToken cancellationToken)
    {
        _readMemory = readMemory;
        _readBusy = readBusy;
        _stopUnder = stopUnder;
        _time = time;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Before = readMemory();
        Lowest = Before?.Available;
        _busyBefore = readBusy();
        _started = time.GetTimestamp();
        _timer = time.CreateTimer(_ => Tick(), null, interval, interval);
    }

    /// <summary>Gets the token the run's copies use; cancelled when memory runs low, or with the run.</summary>
    public CancellationToken Token => _stop.Token;

    /// <summary>Gets the memory before the run started, or null when it can't be read.</summary>
    public MemorySnapshot? Before { get; }

    /// <summary>Gets the least free memory seen, or null when it can't be read.</summary>
    public long? Lowest { get; private set; }

    /// <summary>Gets a value indicating whether the run was stopped because memory ran low.</summary>
    public bool Tripped { get; private set; }

    /// <summary>Gets the cores the host kept busy on average over the run, once disposed, or null when it can't be read.</summary>
    public double? HostCores { get; private set; }

    /// <summary>Takes one memory reading, and stops the run when too little memory is free.</summary>
    public void Sample()
    {
        lock (_gate)
        {
            if (_readMemory() is not { } now)
            {
                return;
            }

            Lowest = Math.Min(Lowest ?? now.Available, now.Available);
            if (!Tripped && now.Available < _stopUnder(now))
            {
                Tripped = true;
                _stop.Cancel();
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Completes once any running callback has returned, so the last sample counts.
        await _timer.DisposeAsync();
        var seconds = _time.GetElapsedTime(_started).TotalSeconds;
        HostCores = _busyBefore is { } before && _readBusy() is { } after && seconds > 0 ? Math.Max(0, after - before) / seconds : null;
        _stop.Dispose();
    }

    /// <summary>Takes one sample from the timer, unless one is already running.</summary>
    private void Tick()
    {
        if (!_gate.TryEnter())
        {
            return;
        }

        try
        {
            Sample();
        }
        finally
        {
            _gate.Exit();
        }
    }
}
