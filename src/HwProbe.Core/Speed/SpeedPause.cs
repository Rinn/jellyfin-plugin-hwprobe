namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Pauses a speed run between measurements: the one running finishes, and the next waits until resumed.</summary>
/// <param name="time">Clock for the time spent paused.</param>
public sealed class SpeedPause(TimeProvider time)
{
    private readonly Lock _lock = new();
    private TaskCompletionSource? _resumed;
    private DateTimeOffset? _waitingSince;
    private TimeSpan _paused;

    /// <summary>Gets a value indicating whether a pause is asked for.</summary>
    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _resumed is not null;
            }
        }
    }

    /// <summary>Gets a value indicating whether the run is waiting, rather than finishing its current measurement first.</summary>
    public bool IsWaiting
    {
        get
        {
            lock (_lock)
            {
                return _waitingSince is not null;
            }
        }
    }

    /// <summary>Gets the time spent waiting so far, so the time left can leave it out.</summary>
    public TimeSpan Paused
    {
        get
        {
            lock (_lock)
            {
                return _paused + (_waitingSince is { } since ? time.GetUtcNow() - since : TimeSpan.Zero);
            }
        }
    }

    /// <summary>Asks the run to wait after its current measurement.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            _resumed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Lets the run go on.</summary>
    public void Resume()
    {
        TaskCompletionSource? resumed;
        lock (_lock)
        {
            (resumed, _resumed) = (_resumed, null);
        }

        resumed?.TrySetResult();
    }

    /// <summary>Waits while paused; returns at once otherwise.</summary>
    /// <param name="cancellationToken">Cancels the wait, and with it the run.</param>
    /// <returns>A task that completes when the run may go on.</returns>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        Task resumed;
        lock (_lock)
        {
            if (_resumed is null)
            {
                return;
            }

            resumed = _resumed.Task;
            _waitingSince = time.GetUtcNow();
        }

        try
        {
            await resumed.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (_lock)
            {
                _paused += time.GetUtcNow() - _waitingSince!.Value;
                _waitingSince = null;
            }
        }
    }
}
