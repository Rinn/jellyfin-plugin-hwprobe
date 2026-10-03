using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.Probing;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>Single-flight, busy refusal and report saving in <see cref="ProbeService"/>.</summary>
[Trait("Category", "Unit")]
public sealed class ProbeServiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hwprobe-plugin-").FullName;

    /// <summary>A completed probe saves its report and records timestamps.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CompletedProbeSavesReport()
    {
        using var service = Create(_ => Task.FromResult(Reports.Sample()), transcoding: false);

        var result = await service.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProbeRunResult.Completed, result);
        var json = await service.LatestJsonAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Reports.Sample().Fingerprint, ReportStore.Deserialize(json!)!.Fingerprint);
        Assert.Equal(ProbeState.Idle, service.Status.State);
        Assert.NotNull(service.Status.LastCompletedUtc);
        Assert.Null(service.Status.LastError);
    }

    /// <summary>A report saved by another HwProbe version, such as before an update, isn't shown.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReportFromAnotherVersionIsCleared()
    {
        using var service = Create(_ => Task.FromResult(Reports.Sample() with { HwProbeVersion = "0.0.1.0" }), transcoding: false);
        await service.RunAsync(TestContext.Current.CancellationToken);

        Assert.Null(await service.LatestJsonAsync(TestContext.Current.CancellationToken));

        // Left for the next probe to overwrite; deleting it on read could race a probe that's saving.
        Assert.True(File.Exists(Path.Combine(_directory, "latest.json")));
    }

    /// <summary>A report from another ffmpeg path or version isn't shown; the same version in another form is.</summary>
    /// <param name="path">The server's ffmpeg path now.</param>
    /// <param name="version">The server's ffmpeg version now, or null when unreadable.</param>
    /// <param name="kept">Whether the report is kept.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("/usr/lib/jellyfin-ffmpeg/ffmpeg", "8.1.2", true)]
    [InlineData("/usr/lib/jellyfin-ffmpeg/ffmpeg", "8.1.2.0", true)]
    [InlineData("/usr/lib/jellyfin-ffmpeg/ffmpeg", null, true)]
    [InlineData("/usr/lib/jellyfin-ffmpeg/ffmpeg", "8.1.3", false)]
    [InlineData("/usr/bin/ffmpeg", "8.1.2", false)]
    public async Task ReportFromAnotherFfmpegIsCleared(string path, string? version, bool kept)
    {
        using var service = new ProbeService(_ => Task.FromResult(Reports.Sample()), () => false, Path.Combine(_directory, "latest.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance)
        {
            CurrentFfmpeg = () => (path, version is null ? null : Version.Parse(version)),
        };
        await service.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(kept, await service.LatestJsonAsync(TestContext.Current.CancellationToken) is not null);
    }

    /// <summary>The diagnostics zip is offered only beside the report it was made with, and only when that report is shown.</summary>
    /// <param name="version">The HwProbe version the report records, or null for this one.</param>
    /// <param name="zip">Whether a zip was saved.</param>
    /// <param name="sameProbe">Whether the zip holds the shown report rather than an earlier one.</param>
    /// <param name="offered">Whether the zip is offered.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(null, true, true, true)]
    [InlineData(null, false, true, false)]
    [InlineData(null, true, false, false)]
    [InlineData("0.0.1.0", true, true, false)]
    public async Task DiagnosticsFollowTheReport(string? version, bool zip, bool sameProbe, bool offered)
    {
        var report = version is null ? Reports.Sample() : Reports.Sample() with { HwProbeVersion = version };
        using var service = Create(_ => Task.FromResult(report), transcoding: false);
        await service.RunAsync(TestContext.Current.CancellationToken);
        if (zip)
        {
            var bundled = sameProbe ? report : report with { GeneratedUtc = report.GeneratedUtc.AddHours(-1) };
            await DiagnosticsBundle.WriteAsync(service.DiagnosticsPath, bundled, [], TestContext.Current.CancellationToken);
        }

        Assert.Equal(offered, await service.LatestDiagnosticsAsync(TestContext.Current.CancellationToken) is not null);
    }

    /// <summary>A speed run needs a report, refuses unknown names, and saves its report over the viable backends.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SpeedRunMeasuresViableBackends()
    {
        var ct = TestContext.Current.CancellationToken;
        IReadOnlyCollection<(HwType Type, string Device)>? measured = null;
        SpeedOptions? asked = null;
        using var service = new ProbeService(_ => Task.FromResult(Reports.Sample()), () => false, Path.Combine(_directory, "latest.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance)
        {
            MeasureSpeed = (speed, backends, progress, _) =>
            {
                (asked, measured) = (speed, backends);
                var result = new SpeedResult(HwType.none, string.Empty, "pattern|h264-8mbps", string.Empty, 300, 12, false, null);
                progress.Report(new SpeedProgress(1, 1, result));
                return Task.FromResult(new SpeedReport(DateTimeOffset.UnixEpoch, Reports.Sample().Ffmpeg, speed.Method, [result]));
            },
            ServerSpeedSettings = () => new SpeedSettings { EncoderPreset = "fast" },
        };
        var request = new SpeedRequest("full", [], []) { Backends = ["none"], Repeats = 2, TimeLimitSeconds = 60, Options = new Dictionary<string, string> { ["EncodingThreadCount"] = "4", ["H264Crf"] = "20", ["Audio"] = "copy" } };

        Assert.Equal(ProbeRunResult.NoReport, await service.StartSpeedAsync(request, ct));
        await service.RunAsync(ct);
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Method = "fastest" }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Videos = ["8k-h266"] }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Outputs = ["8k-h266"] }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Videos = ["library"] }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Repeats = 4 }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { TimeLimitSeconds = 5 }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Options = new Dictionary<string, string> { ["EncodingThreadCount"] = "99" } }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Options = new Dictionary<string, string> { ["EncoderPreset"] = "placebo" } }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Options = new Dictionary<string, string> { ["H265Crf"] = "52" } }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Options = new Dictionary<string, string> { ["AudioVbr"] = "yes" } }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Options = new Dictionary<string, string> { ["Nonsense"] = "true" } }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Backends = ["cuda"] }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Backends = [] }, ct));

        Assert.Equal(ProbeRunResult.Started, await service.StartSpeedAsync(request, ct));
        await service.Background;

        Assert.Equal(Reports.Sample().Backends.Where(b => b.Verdict == BackendVerdict.Viable).Select(b => (b.Type, b.Device)), measured);
        Assert.Equal((SpeedMethod.Full, "fast", 4), (asked!.Method, asked.Settings.EncoderPreset, asked.Settings.EncodingThreadCount));
        Assert.Equal([HwType.none], asked.Backends);
        Assert.Equal((SpeedCatalog.DefaultVideos, SpeedCatalog.DefaultOutputs), (asked.Videos, asked.Outputs));
        Assert.Equal((20, 28, true), (asked.Settings.H264Crf, asked.Settings.H265Crf, asked.Settings.AudioCopy));
        Assert.Equal((2, TimeSpan.FromMinutes(1)), (asked.Repeats, asked.TimeLimit!.Value));
        Assert.Null(service.RunningSpeedJson());
        Assert.Equal(ProbeActivity.Speed, service.Status.Activity);
        Assert.Null(service.Status.Total);
        Assert.Equal(12, SpeedReportStore.Deserialize((await service.LatestSpeedJsonAsync(ct))!)!.Results[0].Streams);

        var history = await service.SpeedHistoryAsync(ct);
        Assert.Equal([("19700101T000000Z", "Full", 1, true)], history.Select(h => (h.Id, h.Method, h.Tests, h.Current)));
        Assert.NotNull(await service.SpeedHistoryJsonAsync("19700101T000000Z", ct));
        Assert.Null(await service.SpeedHistoryJsonAsync("../latest", ct));

        Assert.Equal(DeleteOutcome.NotFound, await service.DeleteSpeedHistoryAsync("../latest", ct));
        Assert.Equal(DeleteOutcome.NotFound, await service.DeleteSpeedHistoryAsync("20000101T000000Z", ct));
        Assert.Equal(DeleteOutcome.Deleted, await service.DeleteSpeedHistoryAsync("19700101T000000Z", ct));
        Assert.Empty(await service.SpeedHistoryAsync(ct));
        Assert.Null(await service.LatestSpeedJsonAsync(ct));
        Assert.Equal(DeleteOutcome.Deleted, await service.DeleteSpeedHistoryAsync(null, ct));
    }

    /// <summary>A running speed run shows its plan as it fills in, pauses after the current measurement, and keeps what's finished when cancelled.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SpeedRunPausesAndCancels()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proceed = new SemaphoreSlim(0);
        using var service = new ProbeService(_ => Task.FromResult(Reports.Sample()), () => false, Path.Combine(_directory, "latest.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance)
        {
            MeasureSpeed = async (speed, backends, progress, token) =>
            {
                SpeedResult Planned(string test) => new(HwType.none, string.Empty, test, string.Empty, null, null, false, null) { Pending = true };
                progress.Report(new SpeedProgress(0, 2, null) { Planned = [Planned("pattern|h264-8mbps"), Planned("pattern|decode")] });
                progress.Report(new SpeedProgress(0, 2, null) { Preparing = "Generating Test video, H.264" });
                progress.Report(new SpeedProgress(0, 2, null));
                var done = new SpeedResult(HwType.none, string.Empty, "pattern|h264-8mbps", string.Empty, 300, 12, false, null);
                progress.Report(new SpeedProgress(1, 2, done));
                first.SetResult();
                await proceed.WaitAsync(token);
                try
                {
                    await speed.Pause!.WaitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    return new SpeedReport(DateTimeOffset.UnixEpoch, Reports.Sample().Ffmpeg, speed.Method, [done]) { Cancelled = true };
                }

                throw new InvalidOperationException("Resumed instead of cancelled.");
            },
            ServerSpeedSettings = () => new SpeedSettings(),
        };
        await service.RunAsync(ct);

        Assert.Equal(ProbeRunResult.Started, await service.StartSpeedAsync(new SpeedRequest("confirm", [], []), ct));
        await first.Task;
        var running = SpeedReportStore.Deserialize(service.RunningSpeedJson()!)!;
        Assert.Equal([(false, 12), (true, (int?)null)], running.Results.Select(r => (r.Pending, r.Streams)));
        Assert.Equal(SpeedPhase.Measuring, service.Status.Phase);
        Assert.NotNull(service.Status.MeasuringSeconds);

        Assert.True(service.PauseSpeed(true));
        Assert.Equal(SpeedPhase.Pausing, service.Status.Phase);
        proceed.Release();
        Assert.True(SpinWait.SpinUntil(() => service.Status.Phase == SpeedPhase.Paused, TimeSpan.FromSeconds(10)));

        Assert.True(service.CancelSpeed());
        await service.Background;

        var saved = SpeedReportStore.Deserialize((await service.LatestSpeedJsonAsync(ct))!)!;
        Assert.True(saved.Cancelled);
        Assert.Single(saved.Results);
        Assert.False(service.CancelSpeed());
        Assert.Equal((ProbeState.Idle, (SpeedPhase?)null), (service.Status.State, service.Status.Phase));
    }

    /// <summary>A speed report from another HwProbe version isn't shown.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SpeedFromAnotherVersionIsCleared()
    {
        using var service = Create(_ => Task.FromResult(Reports.Sample()), transcoding: false);
        await SpeedReportStore.WriteAsync(new SpeedReport(DateTimeOffset.UnixEpoch, Reports.Sample().Ffmpeg, SpeedMethod.Quick, []) { HwProbeVersion = "0.0.1.0" }, service.SpeedPath, TestContext.Current.CancellationToken);

        Assert.Null(await service.LatestSpeedJsonAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The clip cache reports its size, and is deleted only when nothing is using it.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CacheIsSizedAndPurged()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixtures = Path.Combine(_directory, "fixtures");
        Directory.CreateDirectory(Path.Combine(fixtures, "samples"));
        await File.WriteAllBytesAsync(Path.Combine(fixtures, "samples", "a.mkv"), new byte[1500], ct);
        using var release = new SemaphoreSlim(0);
        using var service = new ProbeService(
            async c =>
            {
                await release.WaitAsync(c);
                return Reports.Sample();
            },
            () => false,
            Path.Combine(_directory, "latest.json"),
            TimeProvider.System,
            TimeSpan.Zero,
            NullLogger.Instance)
        {
            FixturesDirectory = fixtures,
        };

        Assert.Equal(new CacheSize(1500, 1), service.FixtureCacheSize());
        var probe = service.RunAsync(ct);
        Assert.False(await service.PurgeFixtureCacheAsync(ct));
        release.Release();
        await probe;

        Assert.True(await service.PurgeFixtureCacheAsync(ct));
        Assert.Equal(new CacheSize(0, 0), service.FixtureCacheSize());
    }

    /// <summary>Nothing runs while a session is transcoding.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task RefusesWhileTranscoding()
    {
        var ran = false;
        using var service = Create(
            _ =>
            {
                ran = true;
                return Task.FromResult(Reports.Sample());
            },
            transcoding: true);

        Assert.Equal(ProbeRunResult.ServerBusy, await service.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ProbeRunResult.ServerBusy, await service.StartAsync(TestContext.Current.CancellationToken));
        Assert.False(ran);
    }

    /// <summary>Direct play and a remux don't count as busy; re-encoding video or audio does.</summary>
    [Fact]
    public void RemuxIsNotBusy()
    {
        Assert.False(ProbeService.IsTranscoding(null));
        Assert.False(ProbeService.IsTranscoding(new TranscodingInfo { IsVideoDirect = true, IsAudioDirect = true }));
        Assert.True(ProbeService.IsTranscoding(new TranscodingInfo { IsVideoDirect = true, IsAudioDirect = false }));
        Assert.True(ProbeService.IsTranscoding(new TranscodingInfo { IsVideoDirect = false, IsAudioDirect = true }));
    }

    /// <summary>A second request while one runs is refused, not queued.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SecondRequestWhileRunningIsRefused()
    {
        using var release = new SemaphoreSlim(0);
        using var service = Create(
            async ct =>
            {
                await release.WaitAsync(ct);
                return Reports.Sample();
            },
            transcoding: false);

        var first = service.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProbeState.Running, service.Status.State);
        Assert.Equal(ProbeRunResult.AlreadyRunning, await service.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ProbeRunResult.AlreadyRunning, await service.StartAsync(TestContext.Current.CancellationToken));

        release.Release();
        Assert.Equal(ProbeRunResult.Completed, await first);
    }

    /// <summary>A failing probe is recorded and doesn't throw, and the next probe can run.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FailureIsRecordedNotThrown()
    {
        var fail = true;
        using var service = Create(_ => fail ? throw new InvalidOperationException("ffmpeg vanished") : Task.FromResult(Reports.Sample()), transcoding: false);

        Assert.Equal(ProbeRunResult.Failed, await service.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ffmpeg vanished", service.Status.LastError);

        fail = false;
        Assert.Equal(ProbeRunResult.Completed, await service.RunAsync(TestContext.Current.CancellationToken));
        Assert.Null(service.Status.LastError);
    }

    /// <summary>No report reads as null before any probe completes.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task NoReportBeforeFirstProbe()
    {
        using var service = Create(_ => Task.FromResult(Reports.Sample()), transcoding: false);

        Assert.Null(await service.LatestJsonAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A transcode that appears only on the second check still blocks the probe.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SecondCheckCatchesALateTranscode()
    {
        var checks = 0;
        var ran = false;
        using var service = new ProbeService(
            _ =>
            {
                ran = true;
                return Task.FromResult(Reports.Sample());
            },
            () => ++checks % 2 == 0,
            Path.Combine(_directory, "latest.json"),
            TimeProvider.System,
            TimeSpan.Zero,
            NullLogger.Instance);

        Assert.Equal(ProbeRunResult.ServerBusy, await service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, checks);
        Assert.False(ran);
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Creates a service with a scripted probe.</summary>
    /// <param name="probe">The probe.</param>
    /// <param name="transcoding">Whether a session is transcoding.</param>
    /// <returns>The service.</returns>
    private ProbeService Create(Func<CancellationToken, Task<CapabilityReport>> probe, bool transcoding) =>
        new(probe, () => transcoding, Path.Combine(_directory, "latest.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance);
}
