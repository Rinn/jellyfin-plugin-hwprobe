using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>fps and stream counting in <see cref="SpeedMeter"/>, over scripted launches.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedMeterTests
{
    /// <summary>Quick measures one copy's fps and counts no streams, since one copy's speed doesn't say how many keep up together.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task QuickMeasuresSpeedOnly()
    {
        var host = new Host(capacity: 5, fps: 130);

        var measured = await SpeedMeter.MeasureAsync(host.LaunchAsync, SpeedMethod.Quick, 24, countStreams: true, TestContext.Current.CancellationToken);

        Assert.Equal(130, measured.Fps!.Value, 1);
        Assert.Null(measured.Streams);

        // One copy, then one again with more content, since the first finished in under 5 s.
        Assert.Equal([1, 1], host.Copies);
    }

    /// <summary>Confirm starts from one copy's speed and searches up from a count that keeps up, or down from one that doesn't.</summary>
    /// <param name="capacity">How many copies the scripted host keeps at real time.</param>
    /// <param name="fps">One copy's fps.</param>
    /// <param name="expected">The streams reported.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(5, 130, 5)]
    [InlineData(3, 130, 3)]
    [InlineData(11, 130, 11)]
    [InlineData(40, 130, 16)]
    [InlineData(0, 10, 0)]
    public async Task ConfirmSearchesFromTheSpeed(int capacity, double fps, int expected)
    {
        var host = new Host(capacity, fps);

        var measured = await SpeedMeter.MeasureAsync(host.LaunchAsync, SpeedMethod.Confirm, 24, countStreams: true, TestContext.Current.CancellationToken);

        Assert.Equal(expected, measured.Streams);
        Assert.Equal(expected == SpeedMeter.MaxStreams, measured.Capped);
    }

    /// <summary>Full ramps up by doubling and searches between the last pass and the first failure.</summary>
    /// <param name="capacity">How many copies keep real time.</param>
    /// <param name="expected">The streams reported.</param>
    /// <param name="capped">Whether the count hit the cap.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(6, 6, false)]
    [InlineData(11, 11, false)]
    [InlineData(40, 16, true)]
    public async Task FullFindsTheCapacity(int capacity, int expected, bool capped)
    {
        var host = new Host(capacity, fps: 50);

        var measured = await SpeedMeter.MeasureAsync(host.LaunchAsync, SpeedMethod.Full, 24, countStreams: true, TestContext.Current.CancellationToken);

        Assert.Equal(expected, measured.Streams);
        Assert.Equal(capped, measured.Capped);
    }

    /// <summary>At the time limit the count stops at what's confirmed, or none when nothing is; the single run always happens.</summary>
    /// <param name="method">Confirm or full.</param>
    /// <param name="runsAllowed">Runs of copies started before the limit passes.</param>
    /// <param name="expected">The streams reported.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(SpeedMethod.Confirm, 0, null)]
    [InlineData(SpeedMethod.Full, 0, null)]
    [InlineData(SpeedMethod.Full, 2, 2)]
    [InlineData(SpeedMethod.Confirm, 1, 5)]
    public async Task TimeLimitReportsWhatItHas(SpeedMethod method, int runsAllowed, int? expected)
    {
        var host = new Host(capacity: 11, fps: 130);

        var measured = await SpeedMeter.MeasureAsync(host.LaunchAsync, method, 24, countStreams: true, TestContext.Current.CancellationToken, () => host.Copies.Count >= 2 + runsAllowed);

        Assert.Equal(130, measured.Fps!.Value, 1);
        Assert.Equal(expected, measured.Streams);
        Assert.StartsWith("Time limit reached", measured.Note, StringComparison.Ordinal);
        Assert.Equal(2 + runsAllowed, host.Copies.Count);
    }

    /// <summary>A fast single run is repeated with more content, and its fps comes from the longer run.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ShortRunIsRepeatedLonger()
    {
        var host = new Host(capacity: 16, fps: 2400);

        var measured = await SpeedMeter.MeasureAsync(host.LaunchAsync, SpeedMethod.Quick, 24, countStreams: true, TestContext.Current.CancellationToken);

        Assert.Equal(2, host.Contents.Count);
        Assert.True(host.Contents[1] > host.Contents[0] * 5);
        Assert.Equal(2400, measured.Fps!.Value, 1);
    }

    /// <summary>A decode test reports fps only.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task DecodeReportsFpsOnly()
    {
        var host = new Host(capacity: 5, fps: 130);

        var measured = await SpeedMeter.MeasureAsync(host.LaunchAsync, SpeedMethod.Full, 24, countStreams: false, TestContext.Current.CancellationToken);

        Assert.Null(measured.Streams);
        Assert.All(host.Copies, c => Assert.Equal(1, c));
    }

    /// <summary>A run cut off by its timeout still gives fps from the frames it reached; a failed one gives a reason.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TimeoutAndFailure()
    {
        Task<IReadOnlyList<FfmpegRunResult>> TimedOutAsync(int copies, TimeSpan content, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FfmpegRunResult>>([new(FfmpegRunStatus.TimedOut, null, string.Empty, string.Empty, 60, TimeSpan.FromSeconds(30), null)]);
        Task<IReadOnlyList<FfmpegRunResult>> FailedAsync(int copies, TimeSpan content, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FfmpegRunResult>>([new(FfmpegRunStatus.Exited, 1, string.Empty, "x\nNo such filter: 'scale_vt'\n", null, TimeSpan.FromSeconds(0.2), null)]);

        var slow = await SpeedMeter.MeasureAsync(TimedOutAsync, SpeedMethod.Confirm, 24, countStreams: true, TestContext.Current.CancellationToken);
        var broken = await SpeedMeter.MeasureAsync(FailedAsync, SpeedMethod.Confirm, 24, countStreams: true, TestContext.Current.CancellationToken);

        Assert.Equal(2, slow.Fps!.Value, 1);
        Assert.Equal(0, slow.Streams);
        Assert.Null(broken.Fps);
        Assert.Equal("ffmpeg exited with 1: No such filter: 'scale_vt'", broken.Note);
    }

    /// <summary>Copies that fail to start rather than fall behind are reported as a likely session limit.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SessionLimitIsNamed()
    {
        Task<IReadOnlyList<FfmpegRunResult>> LimitedAsync(int copies, TimeSpan content, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FfmpegRunResult>>([.. Enumerable.Range(0, copies).Select(i => i < 3
                ? new FfmpegRunResult(FfmpegRunStatus.Exited, 0, string.Empty, string.Empty, (long)(content.TotalSeconds * 24), TimeSpan.FromSeconds(copies == 1 ? 6 : 9), null)
                : new FfmpegRunResult(FfmpegRunStatus.Exited, 1, string.Empty, "OpenEncodeSessionEx failed: incompatible client key (21)", null, TimeSpan.FromSeconds(0.3), null))]);

        var measured = await SpeedMeter.MeasureAsync(LimitedAsync, SpeedMethod.Full, 24, countStreams: true, TestContext.Current.CancellationToken);

        Assert.Equal(3, measured.Streams);
        Assert.Equal("4 at once failed to start, likely the driver's limit on sessions rather than speed.", measured.Note);
    }

    /// <summary>When the longer single run is slower (a looped clip restarting), the faster first run's fps is kept.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FasterRunIsKept()
    {
        var calls = 0;
        Task<IReadOnlyList<FfmpegRunResult>> SlowerLaterAsync(int copies, TimeSpan content, CancellationToken ct)
        {
            var seconds = ++calls == 1 ? 1.0 : 30.0;
            return Task.FromResult<IReadOnlyList<FfmpegRunResult>>([new(FfmpegRunStatus.Exited, 0, string.Empty, string.Empty, 240, TimeSpan.FromSeconds(seconds), null)]);
        }

        var measured = await SpeedMeter.MeasureAsync(SlowerLaterAsync, SpeedMethod.Quick, 24, countStreams: false, TestContext.Current.CancellationToken);

        Assert.Equal(240, measured.Fps!.Value, 1);
    }

    /// <summary>A scripted host that keeps a fixed number of copies at real time.</summary>
    /// <param name="capacity">Copies that finish within real time at once.</param>
    /// <param name="fps">One copy's fps when alone.</param>
    private sealed class Host(int capacity, double fps)
    {
        /// <summary>Gets the copies asked for by each launch.</summary>
        public List<int> Copies { get; } = [];

        /// <summary>Gets the content asked for by each launch.</summary>
        public List<TimeSpan> Contents { get; } = [];

        /// <summary>Scripts a launch.</summary>
        /// <param name="copies">Copies started together.</param>
        /// <param name="content">Content each processes.</param>
        /// <param name="cancellationToken">Unused.</param>
        /// <returns>One result per copy.</returns>
        public Task<IReadOnlyList<FfmpegRunResult>> LaunchAsync(int copies, TimeSpan content, CancellationToken cancellationToken)
        {
            Copies.Add(copies);
            Contents.Add(content);
            var frames = (long)(content.TotalSeconds * 24);
            var seconds = copies > capacity ? content.TotalSeconds * 1.5 : copies == 1 ? frames / fps : content.TotalSeconds * 0.9;
            return Task.FromResult<IReadOnlyList<FfmpegRunResult>>([.. Enumerable.Repeat(new FfmpegRunResult(FfmpegRunStatus.Exited, 0, string.Empty, string.Empty, frames, TimeSpan.FromSeconds(seconds), null), copies)]);
        }
    }
}
