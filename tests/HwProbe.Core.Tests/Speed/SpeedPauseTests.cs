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
}
