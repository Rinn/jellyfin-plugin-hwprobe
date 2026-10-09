using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Resources;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>The memory and CPU caps on copies run at once, over scripted readings.</summary>
[Trait("Category", "Unit")]
public sealed class CopyBudgetTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly CatalogConcurrency _limits = new() { MemoryReserveShare = 0.25, MemoryReserveMinimumMiB = 1536, MemoryMargin = 1.5, MemoryStopShare = 0.5, CpuShare = 0.75 };

    private MemorySnapshot? _memory = new(7 * GiB, 8 * GiB);

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

    /// <summary>Before one copy has run, nothing is known, so nothing is capped.</summary>
    [Fact]
    public void NothingKnownCapsNothing() =>
        Assert.Null(new CopyBudget(() => _memory, () => null, 12, _limits, TimeProvider.System).MostCopies(speed: 3));

    /// <summary>A run is stopped as soon as free memory falls under half the reserve, and the stop is recorded.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LowMemoryStopsTheRun()
    {
        var budget = new CopyBudget(() => _memory, () => null, 12, _limits, TimeProvider.System);
        var watch = budget.Watch(TestContext.Current.CancellationToken);

        _memory = new MemorySnapshot(GiB / 2, 8 * GiB);
        watch.Sample();
        var stopped = watch.Token.IsCancellationRequested;
        await watch.DisposeAsync();
        budget.Finish(watch, [CopyBudget.Stopped, CopyBudget.Stopped]);

        Assert.True(stopped);
        Assert.True(budget.LastStopped);
    }

    /// <summary>Runs one copy through a budget: free memory drops by the copy's bytes while it runs, and it reports its CPU time.</summary>
    /// <param name="dropBytes">The memory the copy takes.</param>
    /// <param name="cpuSeconds">Its CPU time.</param>
    /// <param name="seconds">Its wall time.</param>
    /// <returns>The budget afterwards, with free memory back where it was.</returns>
    private async Task<CopyBudget> AfterOneCopyAsync(long dropBytes, double cpuSeconds, double seconds)
    {
        var before = _memory ?? throw new InvalidOperationException("No memory reading.");
        var budget = new CopyBudget(() => _memory, () => null, 12, _limits, TimeProvider.System);
        var watch = budget.Watch(TestContext.Current.CancellationToken);
        _memory = before with { Available = before.Available - dropBytes };
        watch.Sample();
        _memory = before;
        await watch.DisposeAsync();
        var run = new FfmpegRunResult(FfmpegRunStatus.Exited, 0, string.Empty, string.Empty, 240, TimeSpan.FromSeconds(seconds), null) { Resources = new ResourceUsage(seconds, cpuSeconds, null) };
        budget.Finish(watch, [run]);
        return budget;
    }
}
