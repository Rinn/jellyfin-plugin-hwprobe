using Jellyfin.Plugin.HwProbe.Core.Resources;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Resources;

/// <summary>Sampling on a timer in <see cref="SampledResourceMonitor"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SampledResourceMonitorTests
{
    /// <summary>A sample that throws is caught on the timer thread, which would otherwise end the process, and the figures are left out.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FailedSampleLeavesFiguresOut()
    {
        using var monitor = new Throwing();

        await monitor.Sampled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await monitor.StopAsync();

        Assert.Null(monitor.Finish(1).CpuSeconds);
    }

    /// <summary>A monitor whose every sample throws.</summary>
    private sealed class Throwing : SampledResourceMonitor
    {
        /// <summary>Initializes a new instance of the <see cref="Throwing"/> class and starts sampling.</summary>
        public Throwing() => Begin();

        /// <summary>Gets a task that completes when the first sample has run.</summary>
        public TaskCompletionSource Sampled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc/>
        public override ResourceUsage Finish(double seconds) => new(seconds, Failed ? null : 1, null);

        /// <inheritdoc/>
        protected override void Sample()
        {
            Sampled.TrySetResult();
            throw new InvalidOperationException("sample failed");
        }
    }
}
