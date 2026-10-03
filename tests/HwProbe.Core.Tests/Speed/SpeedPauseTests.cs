using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Waiting between measurements in <see cref="SpeedPause"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedPauseTests
{
    /// <summary>A pause holds the next wait until resumed, and the time waiting is counted.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task WaitsUntilResumed()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ManualClock();
        var pause = new SpeedPause(time);

        await pause.WaitAsync(ct);
        pause.Pause();
        var waiting = pause.WaitAsync(ct);

        Assert.False(waiting.IsCompleted);
        Assert.True(pause.IsWaiting);
        time.Advance(TimeSpan.FromSeconds(30));
        pause.Resume();
        await waiting;

        Assert.False(pause.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(30), pause.Paused);
    }

    /// <summary>Cancelling while paused ends the wait with the cancellation.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CancelEndsTheWait()
    {
        using var cancel = new CancellationTokenSource();
        var pause = new SpeedPause(TimeProvider.System);
        pause.Pause();
        var waiting = Assert.ThrowsAnyAsync<OperationCanceledException>(() => pause.WaitAsync(cancel.Token));

        await cancel.CancelAsync();

        await waiting;
        Assert.False(pause.IsWaiting);
    }

    /// <summary>A run held for a transcode waits until it ends, counts the wait as paused, and doesn't hold without a busy check.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task HoldsWhileBusy()
    {
        var ct = TestContext.Current.CancellationToken;
        var checks = 0;
        var pause = new SpeedPause(TimeProvider.System) { Busy = () => Interlocked.Increment(ref checks) < 4, BusyCheck = TimeSpan.FromMilliseconds(5) };

        var holding = pause.HoldWhileBusyAsync(ct);
        Assert.True(pause.IsHolding);
        await holding;

        Assert.False(pause.IsHolding);
        Assert.True(checks >= 4);
        Assert.True(pause.Paused > TimeSpan.Zero);
        await new SpeedPause(TimeProvider.System).HoldWhileBusyAsync(ct);
    }
}
