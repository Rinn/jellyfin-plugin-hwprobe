using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>fps and stream counting in <see cref="SpeedMeter"/>, over scripted launches.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedMeterTests
{
    /// <summary>A copy keeps up when it runs at real time from its first frame on: slow start-up is neither counted nor excused.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CopiesAreTimedFromTheirFirstFrame()
    {
        Task<IReadOnlyList<FfmpegRunResult>> LaunchAsync(int copies, TimeSpan content, CancellationToken cancellationToken)
        {
            // Every copy takes 2 s to start; up to three hold 24 fps after that, four drop to 23.
            var fps = copies <= 3 ? 24.0 : 23.0;
            var run = copies == 1
                ? new FfmpegRunResult(FfmpegRunStatus.Exited, 0, string.Empty, string.Empty, 240, TimeSpan.FromSeconds(5), null)
                : new FfmpegRunResult(FfmpegRunStatus.Exited, 0, string.Empty, string.Empty, 240, TimeSpan.FromSeconds(2 + (240 / fps)), null) { Timing = new FrameTiming(TimeSpan.FromSeconds(2.5), 12, TimeSpan.FromSeconds(2.5 + (228 / fps)), 240) };
            return Task.FromResult<IReadOnlyList<FfmpegRunResult>>([.. Enumerable.Repeat(run, copies)]);
        }

        var measured = await SpeedMeter.MeasureAsync(LaunchAsync, SpeedMethod.Confirm, 24, countStreams: true, TestContext.Current.CancellationToken);

        Assert.Equal(3, measured.Streams);
    }

    /// <summary>Steady fps leaves out the time before the first report and needs a second of reports to say.</summary>
    [Fact]
    public void SteadyFpsSkipsStartUp()
    {
        Assert.Equal(24, Assert.NotNull(new FrameTiming(TimeSpan.FromSeconds(2), 24, TimeSpan.FromSeconds(12), 264).SteadyFps), 3);
        Assert.Null(new FrameTiming(TimeSpan.FromSeconds(2), 24, TimeSpan.FromSeconds(2.5), 36).SteadyFps);
    }

    /// <summary>Quick measures one copy's fps and counts no streams, since one copy's speed doesn't say how many keep up together.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task QuickMeasuresSpeedOnly()
    {
        var host = new Host(capacity: 5, fps: 130);

        var measured = await SpeedMeter.MeasureAsync(host.LaunchAsync, SpeedMethod.Quick, 24, countStreams: true, TestContext.Current.CancellationToken);

        Assert.Equal(130, Assert.NotNull(measured.Fps), 1);
        Assert.Null(measured.Streams);

        // One copy, then one again with more content, since the first finished in under 5 s.
        Assert.Equal([1, 1], host.Copies);
    }

    /// <summary>Confirm starts from one copy's speed and searches up from a count that keeps up, or down from one that doesn't; Full ramps up by doubling and searches between the last pass and the first failure. Both stop at the cap.</summary>
    /// <param name="method">Confirm or full.</param>
    /// <param name="capacity">How many copies the scripted host keeps at real time.</param>
    /// <param name="fps">One copy's fps.</param>
    /// <param name="expected">The streams reported.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(SpeedMethod.Confirm, 5, 130, 5)]
    [InlineData(SpeedMethod.Confirm, 3, 130, 3)]
    [InlineData(SpeedMethod.Confirm, 11, 130, 11)]
    [InlineData(SpeedMethod.Confirm, 40, 130, SpeedMeter.MaxStreams)]
    [InlineData(SpeedMethod.Confirm, 0, 10, 0)]
    [InlineData(SpeedMethod.Full, 0, 50, 0)]
    [InlineData(SpeedMethod.Full, 1, 50, 1)]
    [InlineData(SpeedMethod.Full, 6, 50, 6)]
    [InlineData(SpeedMethod.Full, 11, 50, 11)]
    [InlineData(SpeedMethod.Full, 40, 50, SpeedMeter.MaxStreams)]
    public async Task FindsTheCapacity(SpeedMethod method, int capacity, double fps, int expected)
    {
        var host = new Host(capacity, fps);

        var measured = await SpeedMeter.MeasureAsync(host.LaunchAsync, method, 24, countStreams: true, TestContext.Current.CancellationToken);

        Assert.Equal(expected, measured.Streams);
        Assert.Equal(expected == SpeedMeter.MaxStreams, measured.Capped);
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

        Assert.Equal(130, Assert.NotNull(measured.Fps), 1);
        Assert.Equal(expected, measured.Streams);
        Assert.StartsWith("Time limit reached", measured.Note, StringComparison.Ordinal);
        Assert.True(measured.Interrupted);
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
        Assert.Equal(2400, Assert.NotNull(measured.Fps), 1);
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

        Assert.Equal(2, Assert.NotNull(slow.Fps), 1);
        Assert.Equal(0, slow.Streams);
        Assert.True(slow.Interrupted);
        Assert.Null(broken.Fps);
        Assert.False(broken.Interrupted);
        Assert.Equal("ffmpeg exited with 1: No such filter: 'scale_vt'", broken.Note);
    }

    /// <summary>A failure's note names the cause rather than the lines that follow from it, and leaves out the memory addresses ffmpeg prints, so the same failure reads the same on every measurement.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FailureNotesNameTheCause()
    {
        // As Homebrew ffmpeg 9.0.2 printed it for aac_at on 96 kHz FLAC.
        static Func<int, TimeSpan, CancellationToken, Task<IReadOnlyList<FfmpegRunResult>>> Failing(string address, string stderr) => (copies, content, ct) =>
            Task.FromResult<IReadOnlyList<FfmpegRunResult>>([new(FfmpegRunStatus.Exited, 171, string.Empty, stderr.Replace("@ ADDRESS", "@ " + address, StringComparison.Ordinal), null, TimeSpan.FromSeconds(0.2), null)]);
        const string AacAt = """
            [aac_at @ ADDRESS] AudioToolbox init error: 1718449215
            [aost#0:0/aac_at @ ADDRESS] [enc:aac_at @ ADDRESS] Error while opening encoder - maybe incorrect parameters such as bit_rate, rate, width or height.
            [af#0:0 @ ADDRESS] Error sending frames to consumers: Unknown error occurred
            [af#0:0 @ ADDRESS] Task finished with error code: -1313558101 (Unknown error occurred)
            [af#0:0 @ ADDRESS] Terminating thread with return code -1313558101 (Unknown error occurred)
            [aost#0:0/aac_at @ ADDRESS] [enc:aac_at @ ADDRESS] Could not open encoder before EOF
            [aost#0:0/aac_at @ ADDRESS] Task finished with error code: -22 (Invalid argument)
            [aost#0:0/aac_at @ ADDRESS] Terminating thread with return code -22 (Invalid argument)
            [out#0/null @ ADDRESS] Nothing was written into output file, because at least one of its streams received no packets.
            """;
        const string Trailer = "[out#0/null @ ADDRESS] Nothing was written into output file, because at least one of its streams received no packets.";

        var first = await SpeedMeter.MeasureAsync(Failing("0x55e590e68280", AacAt), SpeedMethod.Quick, 24, countStreams: false, TestContext.Current.CancellationToken);
        var second = await SpeedMeter.MeasureAsync(Failing("0x5585040b9f00", AacAt), SpeedMethod.Quick, 24, countStreams: false, TestContext.Current.CancellationToken);
        var trailerOnly = await SpeedMeter.MeasureAsync(Failing("0x5585040b9f00", Trailer), SpeedMethod.Quick, 24, countStreams: false, TestContext.Current.CancellationToken);

        Assert.Equal("ffmpeg exited with 171: [aac_at] AudioToolbox init error: 1718449215", first.Note);
        Assert.Equal(first.Note, second.Note);
        Assert.Equal("ffmpeg exited with 171: [out#0/null] Nothing was written into output file, because at least one of its streams received no packets.", trailerOnly.Note);
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

        Assert.Equal(240, Assert.NotNull(measured.Fps), 1);
    }

    /// <summary>An audio run's speed is the content it read over the time taken, as it has no frames; a timed-out one has none.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AudioIsTimedByContent()
    {
        List<TimeSpan> contents = [];
        Task<IReadOnlyList<FfmpegRunResult>> AudioAsync(int copies, TimeSpan content, CancellationToken ct)
        {
            contents.Add(content);
            return Task.FromResult<IReadOnlyList<FfmpegRunResult>>([new(FfmpegRunStatus.Exited, 0, string.Empty, string.Empty, 0, TimeSpan.FromSeconds(content.TotalSeconds / 200), null)]);
        }

        Task<IReadOnlyList<FfmpegRunResult>> TimedOutAsync(int copies, TimeSpan content, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FfmpegRunResult>>([new(FfmpegRunStatus.TimedOut, null, string.Empty, string.Empty, 2, TimeSpan.FromSeconds(30), null)]);

        var measured = await SpeedMeter.MeasureAsync(AudioAsync, SpeedMethod.Full, 1, countStreams: false, TestContext.Current.CancellationToken, pace: MeterPace.Audio);
        var slow = await SpeedMeter.MeasureAsync(TimedOutAsync, SpeedMethod.Full, 1, countStreams: false, TestContext.Current.CancellationToken, pace: MeterPace.Audio);

        Assert.Equal(200, Assert.NotNull(measured.Fps), 1);
        Assert.Null(measured.Streams);
        Assert.Equal(2, contents.Count);
        Assert.Null(slow.Fps);
        Assert.True(slow.Interrupted);
        Assert.Equal("ffmpeg didn't finish before the time limit", slow.Note);
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
