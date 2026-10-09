using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Resources;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>The memory and CPU caps on copies run at once, and the memory guard, over scripted readings and copies.</summary>
[Trait("Category", "Unit")]
public sealed class CopyBudgetTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly CatalogConcurrency _limits = new() { MemoryReserveShare = 0.25, MemoryReserveMinimumMiB = 1536, MemoryMargin = 1.5, MemoryMinimumCopyMiB = 64, MemoryStopShare = 0.5, CpuShare = 0.75 };

    private MemorySnapshot? _memory = new(7 * GiB, 8 * GiB);
    private TaskCompletionSource _seen = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Recorded on an 8 GB container: a 4K QSV copy takes about half a GB no process is charged for, so memory, not the CPU, caps the copies.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MemoryCapsGpuCopies()
    {
        var budget = await AfterOneCopyAsync(dropBytes: GiB / 2, cpuSeconds: 1, seconds: 5);

        // (7 GiB free - 2 GiB reserve) / (0.5 GiB x 1.5) = 6.67.
        Assert.Equal(new CopyLimit(6, false), budget.MostCopies(speed: 14));
    }

    /// <summary>A software copy that keeps 12 cores busy at three times real time needs 4 cores at real time, so 9 of 12 cores allow 2.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CpuCapsSoftwareCopies()
    {
        var budget = await AfterOneCopyAsync(dropBytes: GiB / 10, cpuSeconds: 60, seconds: 5);

        Assert.Equal(new CopyLimit(2, true), budget.MostCopies(speed: 3));
    }

    /// <summary>The reserve is at most half the server's memory, so a 2 GB container isn't held to one copy by the 1.5 GiB minimum.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReserveIsAtMostHalfTheMemory()
    {
        _memory = new MemorySnapshot(GiB * 16 / 10, 2 * GiB);

        var budget = await AfterOneCopyAsync(dropBytes: GiB / 10, cpuSeconds: 1, seconds: 5);

        // (1.6 GiB free - 1 GiB reserve) / (0.1 GiB x 1.5) = 4.
        Assert.Equal(new CopyLimit(4, false), budget.MostCopies(speed: 10));
    }

    /// <summary>Before one copy has run, nothing is known, so nothing is capped.</summary>
    [Fact]
    public void NothingKnownCapsNothing() =>
        Assert.Null(Budget().MostCopies(speed: 3));

    /// <summary>Two copies are stopped once free memory falls under half the reserve; the run reports stand-ins, and the cap after it counts what they took.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LowMemoryStopsCopies()
    {
        var budget = Budget();

        var runs = await budget.RunAsync(2, token => HoldAsync(new MemorySnapshot(GiB / 2, 8 * GiB), token), TestContext.Current.CancellationToken);
        _memory = new MemorySnapshot(7 * GiB, 8 * GiB);

        Assert.Equal([CopyBudget.Stopped, CopyBudget.Stopped], runs);
        Assert.True(budget.LastStopped);

        // The two took 6.5 GiB, 3.25 each: (7 - 2) / (3.25 x 1.5) = 1.03.
        Assert.Equal(new CopyLimit(1, false), budget.MostCopies(speed: 14));
    }

    /// <summary>One copy is never stopped, however little is free: it's what one transcode takes.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OneCopyIsNeverStopped()
    {
        var budget = Budget();

        var runs = await budget.RunAsync(1, token => RunBrieflyAsync(new MemorySnapshot(GiB / 10, 8 * GiB), token), TestContext.Current.CancellationToken);

        Assert.Equal(FfmpegRunStatus.Exited, Assert.Single(runs).Status);
        Assert.False(budget.LastStopped);
    }

    /// <summary>The run's own cancellation still cancels, rather than reading as memory running low.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OwnCancellationStillThrows()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var budget = Budget();

        async Task<FfmpegRunResult> CancelAsync(CancellationToken token)
        {
            await cancel.CancelAsync();
            return await HoldAsync(new MemorySnapshot(7 * GiB, 8 * GiB), token);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => budget.RunAsync(2, CancelAsync, cancel.Token));
    }

    /// <summary>A copy that exited after this much wall and CPU time.</summary>
    /// <param name="seconds">Wall time.</param>
    /// <param name="cpuSeconds">CPU time.</param>
    /// <returns>The result.</returns>
    private static FfmpegRunResult Ran(double seconds, double cpuSeconds) =>
        new(FfmpegRunStatus.Exited, 0, string.Empty, string.Empty, 240, TimeSpan.FromSeconds(seconds), null) { Resources = new ResourceUsage(seconds, cpuSeconds, null) };

    /// <summary>A copy that holds this much memory free until its token is cancelled.</summary>
    /// <param name="during">The memory while it runs.</param>
    /// <param name="token">Its token.</param>
    /// <returns>Never; throws when cancelled.</returns>
    private async Task<FfmpegRunResult> HoldAsync(MemorySnapshot during, CancellationToken token)
    {
        _memory = during;
        await Task.Delay(Timeout.Infinite, token);
        return CopyBudget.Stopped;
    }

    /// <summary>A copy that holds this much memory free for 50 ms, then exits.</summary>
    /// <param name="during">The memory while it runs.</param>
    /// <param name="token">Its token.</param>
    /// <returns>Its result.</returns>
    private async Task<FfmpegRunResult> RunBrieflyAsync(MemorySnapshot during, CancellationToken token)
    {
        _memory = during;
        await Task.Delay(50, token);
        return Ran(1, 1);
    }

    /// <summary>Creates a budget over the scripted memory, for 12 cores, sampling every 5 ms.</summary>
    /// <returns>The budget.</returns>
    private CopyBudget Budget() => new(Read, () => null, 12, _limits, TimeProvider.System, TimeSpan.FromMilliseconds(5));

    /// <summary>Reads the scripted memory, and tells a waiting copy a reading has been taken.</summary>
    /// <returns>The memory now.</returns>
    private MemorySnapshot? Read()
    {
        var now = _memory;
        _seen.TrySetResult();
        return now;
    }

    /// <summary>Runs one copy through a budget: free memory drops by the copy's bytes while it runs, and it reports its CPU time.</summary>
    /// <param name="dropBytes">The memory the copy takes.</param>
    /// <param name="cpuSeconds">Its CPU time.</param>
    /// <param name="seconds">Its wall time.</param>
    /// <returns>The budget afterwards, with free memory back where it was.</returns>
    private async Task<CopyBudget> AfterOneCopyAsync(long dropBytes, double cpuSeconds, double seconds)
    {
        var before = _memory ?? throw new InvalidOperationException("No memory reading.");
        var budget = Budget();
        await budget.RunAsync(
            1,
            async token =>
            {
                // Holds the drop until a reading has seen it, so no timing decides what the budget measures.
                _seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _memory = before with { Available = before.Available - dropBytes };
                await _seen.Task.WaitAsync(token);
                _memory = before;
                return Ran(seconds, cpuSeconds);
            },
            TestContext.Current.CancellationToken);
        return budget;
    }
}
