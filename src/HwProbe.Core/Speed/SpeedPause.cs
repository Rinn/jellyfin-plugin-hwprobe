namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Pauses a speed run between measurements: the one running finishes, and the next waits until resumed.</summary>
/// <param name="time">Clock for the time spent paused.</param>
public sealed class SpeedPause(TimeProvider time)
{
    private readonly Lock _lock = new();
    private TaskCompletionSource? _resumed;
    private DateTimeOffset? _waitingSince;
    private TimeSpan _paused;
    private bool _holding;

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

    /// <summary>Gets a value indicating whether the run is held because the server is transcoding.</summary>
    public bool IsHolding
    {
        get
        {
            lock (_lock)
            {
                return _holding;
            }
        }
    }

    /// <summary>Gets what reports the server transcoding, or null when the run doesn't defer to transcodes.</summary>
    public Func<bool>? Busy { get; init; }

    /// <summary>Gets how often <see cref="Busy"/> is checked.</summary>
    public TimeSpan BusyCheck { get; init; } = TimeSpan.FromSeconds(1);

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

    /// <summary>Waits while the server is transcoding; returns at once otherwise, or when <see cref="Busy"/> is null.</summary>
    /// <param name="cancellationToken">Cancels the wait, and with it the run.</param>
    /// <returns>A task that completes when nothing is transcoding.</returns>
    public async Task HoldWhileBusyAsync(CancellationToken cancellationToken)
    {
        if (Busy is not { } busy || !busy())
        {
            return;
        }

        lock (_lock)
        {
            _holding = true;
            _waitingSince ??= time.GetUtcNow();
        }

        try
        {
            // Two idle checks in a row: a client that logs in again has a session without the transcode for about a second.
            var idle = 0;
            while (idle < 2)
            {
                await Task.Delay(BusyCheck, time, cancellationToken);
                idle = busy() ? 0 : idle + 1;
            }
        }
        finally
        {
            lock (_lock)
            {
                _paused += time.GetUtcNow() - _waitingSince!.Value;
                _waitingSince = null;
                _holding = false;
            }
        }
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
