namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>A monitor that reads counters every few hundred milliseconds, for platforms that forget a process once it exits.</summary>
internal abstract class SampledResourceMonitor : IResourceMonitor
{
    // Often enough that the last sample before exit misses little of a run that lasts seconds.
    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(200);

    // One sample at a time: timer callbacks can overlap when a sample is slow, and Dispose waits for a running one.
    private readonly Lock _gate = new();
    private Timer? _timer;
    private bool _stopped;

    /// <summary>Gets a value indicating whether a sample failed, so the figures are incomplete and are left out.</summary>
    protected bool Failed { get; private set; }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (_timer is { } timer)
        {
            // Completes once any running callback has returned, so Finish sees the last sample.
            await timer.DisposeAsync();
        }
    }

    /// <inheritdoc/>
    public abstract ResourceUsage Finish(double seconds);

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the timer, after any running sample, and anything a subclass holds.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _timer?.Dispose();
        lock (_gate)
        {
            _stopped = true;
        }
    }

    /// <summary>Reads the process's counters.</summary>
    protected abstract void Sample();

    /// <summary>Starts sampling; called once the subclass is ready.</summary>
    protected void Begin() => _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, _interval);

    /// <summary>Takes one sample, unless one is already running or the monitor is stopped.</summary>
    private void Tick()
    {
        if (!_gate.TryEnter())
        {
            return;
        }

        try
        {
            if (!_stopped && !Failed)
            {
                Sample();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception escaping a timer callback would end the server's process; the figures are dropped instead.
            Failed = true;
        }
        finally
        {
            _gate.Exit();
        }
    }
}
