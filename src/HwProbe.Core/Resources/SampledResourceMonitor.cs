namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>A monitor that reads counters every few hundred milliseconds, for platforms that forget a process once it exits.</summary>
internal abstract class SampledResourceMonitor : IResourceMonitor
{
    // Often enough that the last sample before exit misses little of a run that lasts seconds.
    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(200);

    // Timer callbacks can overlap when a sample is slow; one runs at a time.
    private readonly Lock _gate = new();
    private Timer? _timer;

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

    /// <summary>Releases the timer and anything a subclass holds.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Dispose();
        }
    }

    /// <summary>Reads the process's counters.</summary>
    protected abstract void Sample();

    /// <summary>Starts sampling; called once the subclass is ready.</summary>
    protected void Begin() => _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, _interval);

    /// <summary>Takes one sample, unless one is already running.</summary>
    private void Tick()
    {
        if (_gate.TryEnter())
        {
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
}
