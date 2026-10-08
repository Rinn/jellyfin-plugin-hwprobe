using System.IO.Compression;
using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.Probing;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>Single-flight, busy refusal and report saving in <see cref="ProbeService"/>.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
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
        Assert.NotNull(json);
        var report = ReportStore.Deserialize(json);
        Assert.NotNull(report);
        Assert.Equal(Reports.Sample().Fingerprint, report.Fingerprint);
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

        Assert.Equal(offered, await service.LatestDiagnosticsAsync(false, TestContext.Current.CancellationToken) is not null);
    }

    /// <summary>The saved runs and measurements go in the diagnostics zip, as saved, only when asked for; files that aren't saved runs are left out, and missing folders add nothing.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task DiagnosticsIncludeTestResultsWhenAsked()
    {
        var ct = TestContext.Current.CancellationToken;
        var results = Path.Combine(_directory, "results");
        var report = Reports.Sample();
        using var service = new ProbeService(_ => Task.FromResult(report), () => false, Path.Combine(_directory, "latest.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance) { SpeedResultsDirectory = results };
        await service.RunAsync(ct);
        await DiagnosticsBundle.WriteAsync(service.DiagnosticsPath, report, [], ct);

        static Dictionary<string, string> Entries(byte[]? zip)
        {
            Assert.NotNull(zip);
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            return archive.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd(), StringComparer.Ordinal);
        }

        var before = Entries(await service.LatestDiagnosticsAsync(true, ct));
        Directory.CreateDirectory(service.SpeedHistoryDirectory);
        Directory.CreateDirectory(results);
        await File.WriteAllTextAsync(Path.Combine(service.SpeedHistoryDirectory, "20261008T090000Z.json"), "{\"video\":\"Amélie\"}", ct);
        await File.WriteAllTextAsync(Path.Combine(service.SpeedHistoryDirectory, ".20261008T090100Z.json.tmp"), "{}", ct);
        await File.WriteAllTextAsync(Path.Combine(results, "abc.json"), "{\"fps\":2}", ct);

        var without = Entries(await service.LatestDiagnosticsAsync(false, ct));
        var with = Entries(await service.LatestDiagnosticsAsync(true, ct));

        Assert.Equal(without.Keys.Order(StringComparer.Ordinal), before.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(without.Keys.Concat(["measurements/abc.json", "test-results/20261008T090000Z.json"]).Order(StringComparer.Ordinal), with.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("{\"video\":\"Amélie\"}", with["test-results/20261008T090000Z.json"]);
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
        var request = new SpeedRequest("full", [], []) { Backends = ["none"], Repeats = 2, TimeLimitSeconds = 60, MeasureResources = true, Options = new Dictionary<string, string> { ["EncodingThreadCount"] = "4", ["H264Crf"] = "20", ["Audio"] = "copy" } };

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
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Audios = ["dsd"] }, ct));

        Assert.Equal(ProbeRunResult.Started, await service.StartSpeedAsync(request with { Audios = ["flac"], Outputs = ["h264-8mbps", "audio-aac"] }, ct));
        await service.Background;

        Assert.Equal(Reports.Sample().Backends.Where(b => b.Verdict == BackendVerdict.Viable).Select(b => (b.Type, b.Device)), measured);
        Assert.NotNull(asked);
        Assert.Equal((SpeedMethod.Full, "fast", 4), (asked.Method, asked.Settings.EncoderPreset, asked.Settings.EncodingThreadCount));
        Assert.Equal([HwType.none], asked.Backends);
        Assert.True(asked.MeasureResources);

        // Audio inputs alone measure no default video.
        Assert.Empty(asked.Videos);
        Assert.Equal(["h264-8mbps", "audio-aac"], asked.Outputs);
        Assert.Equal(["flac"], asked.Audios);
        Assert.Equal((20, 28, true), (asked.Settings.H264Crf, asked.Settings.H265Crf, asked.Settings.AudioCopy));
        Assert.Equal((2, TimeSpan.FromMinutes(1)), (asked.Repeats, Assert.NotNull(asked.TimeLimit)));
        Assert.Null(service.RunningSpeedJson());
        Assert.Equal(ProbeActivity.Speed, service.Status.Activity);
        Assert.Null(service.Status.Total);
        Assert.Equal(12, SpeedReportFrom(await service.LatestSpeedJsonAsync(ct)).Results[0].Streams);

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

    /// <summary>A suite runs each step with its settings on the suite's backends and labels each saved run, steps finishing within one second included; unknown and unavailable suites are refused.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SuiteRunsEachStep()
    {
        var ct = TestContext.Current.CancellationToken;
        List<SpeedOptions> asked = [];
        var time = DateTimeOffset.UnixEpoch;
        using var service = new ProbeService(_ => Task.FromResult(Reports.Sample()), () => false, Path.Combine(_directory, "latest.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance)
        {
            MeasureSpeed = (speed, backends, progress, _) =>
            {
                asked.Add(speed);
                time = time.AddMilliseconds(100);
                return Task.FromResult(new SpeedReport(time, Reports.Sample().Ffmpeg, speed.Method, [new SpeedResult(HwType.none, string.Empty, "drama|h264-8mbps", string.Empty, 300, 12, false, null)]));
            },
            ServerBackend = () => (HwType.vaapi, "/dev/dri/renderD128"),
        };

        Assert.Equal(ProbeRunResult.NoReport, await service.StartSuiteAsync(new SuiteRequest("presets"), ct));
        await service.RunAsync(ct);
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSuiteAsync(new SuiteRequest("nonsense"), ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSuiteAsync(new SuiteRequest("lowpower"), ct));
        Assert.Equal([("presets", true), ("lowpower", false)], (await service.SuitesAsync(ct)).Where(s => s.Key is "presets" or "lowpower").Select(s => (s.Key, s.Offered)));

        Assert.Equal(ProbeRunResult.Started, await service.StartSuiteAsync(new SuiteRequest("presets") { MeasureResources = true }, ct));
        await service.Background;

        var presets = Catalog.Default.Suites.Single(s => s.Key == "presets");
        Assert.Equal(presets.Steps.Select(s => s.Options["EncoderPreset"] is var p && p == "auto" ? null : p), asked.Select(a => a.Settings.EncoderPreset));
        Assert.All(asked, a => Assert.Equal([HwType.vaapi], a.Backends));
        Assert.All(asked, a => Assert.Equal((SpeedMethod.Confirm, true, true), (a.Method, a.MeasureResources, a.ReuseResults)));
        var history = await service.SpeedHistoryAsync(ct);
        Assert.Equal(presets.Steps.Select(s => s.Label).Reverse(), history.Select(h => h.SuiteStep));
        Assert.All(history, h => Assert.Equal(presets.Name, h.Suite));
        Assert.Null(service.Status.Suite);

        // The audio suite measures its audio inputs in software at its own accuracy, and counts them apart from video.
        var audio = Assert.Single(await service.SuitesAsync(ct), s => s.Key == "audio-formats");
        Assert.Equal((0, SpeedCatalog.Audios.Count * SpeedCatalog.Outputs.Count(o => o.Audio), SpeedMethod.Quick), (audio.Measurements, audio.AudioMeasurements, audio.Method));
        asked.Clear();
        Assert.Equal(ProbeRunResult.Started, await service.StartSuiteAsync(new SuiteRequest("audio-formats"), ct));
        await service.Background;
        var run = Assert.Single(asked);
        Assert.Equal((SpeedMethod.Quick, SpeedCatalog.Audios.Count), (run.Method, run.Audios.Count));
        Assert.Equal([HwType.none], run.Backends);
    }

    /// <summary>A run set to cancel for transcodes refuses to start during one, and stops when one begins, keeping what it measured.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SpeedRunCancelsWhenTheServerTranscodes()
    {
        var ct = TestContext.Current.CancellationToken;
        var transcoding = true;
        var done = new SpeedResult(HwType.none, string.Empty, "pattern|h264-8mbps", string.Empty, 300, 12, false, null);
        using var service = new ProbeService(_ => Task.FromResult(Reports.Sample()), () => transcoding, Path.Combine(_directory, "latest.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance)
        {
            MeasureSpeed = async (speed, backends, progress, token) =>
            {
                transcoding = true;
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                }

                return new SpeedReport(DateTimeOffset.UnixEpoch, Reports.Sample().Ffmpeg, speed.Method, [done]) { Cancelled = true };
            },
            ServerSpeedSettings = () => new SpeedSettings(),
        };
        transcoding = false;
        await service.RunAsync(ct);
        var request = new SpeedRequest("quick", [], []) { WhenTranscoding = TranscodeAction.Cancel };

        transcoding = true;
        Assert.Equal(ProbeRunResult.ServerBusy, await service.StartSpeedAsync(request, ct));
        transcoding = false;
        Assert.Equal(ProbeRunResult.Started, await service.StartSpeedAsync(request, ct));
        await service.Background;

        Assert.True(SpeedReportFrom(await service.LatestSpeedJsonAsync(ct)).CancelledForTranscode);
    }

    /// <summary>A run set to cancel for transcodes stops checking for them when it ends.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TranscodeWatchEndsWithTheRun()
    {
        var ct = TestContext.Current.CancellationToken;
        var checks = 0;
        using var service = new ProbeService(_ => Task.FromResult(Reports.Sample()), () => Interlocked.Increment(ref checks) < 0, Path.Combine(_directory, "latest.json"), new QuickTimeProvider(), TimeSpan.Zero, NullLogger.Instance)
        {
            MeasureSpeed = (speed, backends, progress, token) => Task.FromResult(new SpeedReport(DateTimeOffset.UnixEpoch, Reports.Sample().Ffmpeg, speed.Method, [])),
        };
        await service.RunAsync(ct);

        Assert.Equal(ProbeRunResult.Started, await service.StartSpeedAsync(new SpeedRequest("quick", [], []) { WhenTranscoding = TranscodeAction.Cancel }, ct));
        await service.Background;
        await Task.Delay(100, ct);
        var settled = Volatile.Read(ref checks);
        await Task.Delay(100, ct);

        Assert.Equal(settled, Volatile.Read(ref checks));
    }

    /// <summary>A probe or speed run reads as running as soon as it's started, so the page's next poll follows it.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task StartedRunReadsAsRunningAtOnce()
    {
        var ct = TestContext.Current.CancellationToken;
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
            MeasureSpeed = async (speed, backends, progress, token) =>
            {
                await release.WaitAsync(token);
                return new SpeedReport(DateTimeOffset.UnixEpoch, Reports.Sample().Ffmpeg, speed.Method, []);
            },
        };

        Assert.Equal(ProbeRunResult.Started, await service.StartAsync(ct));
        Assert.Equal((ProbeState.Running, ProbeActivity.Probe), (service.Status.State, service.Status.Activity));
        release.Release();
        await service.Background;

        Assert.Equal(ProbeRunResult.Started, await service.StartSpeedAsync(new SpeedRequest("quick", [], []), ct));
        Assert.Equal((ProbeState.Running, ProbeActivity.Speed), (service.Status.State, service.Status.Activity));
        release.Release();
        await service.Background;
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
                    Assert.NotNull(speed.Pause);
                    await speed.Pause.WaitAsync(token);
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
        var running = SpeedReportFrom(service.RunningSpeedJson());
        Assert.Equal([(false, 12), (true, (int?)null)], running.Results.Select(r => (r.Pending, r.Streams)));
        Assert.Equal(SpeedPhase.Measuring, service.Status.Phase);
        Assert.NotNull(service.Status.ElapsedSeconds);

        Assert.True(service.PauseSpeed(true));
        Assert.Equal(SpeedPhase.Pausing, service.Status.Phase);
        proceed.Release();
        Assert.True(SpinWait.SpinUntil(() => service.Status.Phase == SpeedPhase.Paused, TimeSpan.FromSeconds(10)));

        Assert.True(service.CancelSpeed());
        await service.Background;

        var saved = SpeedReportFrom(await service.LatestSpeedJsonAsync(ct));
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
        using var service = CreateBlocked(release, fixtures);

        Assert.Equal(new CacheSize(1500, 1), service.FixtureCacheSize());
        var probe = service.RunAsync(ct);
        Assert.False(await service.PurgeFixtureCacheAsync(ct));
        release.Release();
        await probe;

        Assert.True(await service.PurgeFixtureCacheAsync(ct));
        Assert.Equal(new CacheSize(0, 0), service.FixtureCacheSize());
    }

    /// <summary>Saved measurements are counted and deleted apart from the clips, and only when nothing is running; deleting the clips keeps them.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SavedMeasurementsAreDeletedApartFromClips()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixtures = Path.Combine(_directory, "fixtures");
        var results = Path.Combine(_directory, "speed-results");
        Directory.CreateDirectory(Path.Combine(fixtures, "samples"));
        Directory.CreateDirectory(results);
        await File.WriteAllBytesAsync(Path.Combine(fixtures, "samples", "a.mkv"), new byte[1500], ct);
        await File.WriteAllTextAsync(Path.Combine(results, "a.json"), "{}", ct);
        await File.WriteAllTextAsync(Path.Combine(results, "b.json"), "{}", ct);
        using var release = new SemaphoreSlim(0);
        using var service = CreateBlocked(release, fixtures, results);

        Assert.True(await service.PurgeFixtureCacheAsync(ct));
        Assert.Equal(2, service.SavedDataSize().Measurements.Files);
        Assert.Equal(4, service.SavedDataSize().Measurements.Bytes);

        var probe = service.RunAsync(ct);
        Assert.False(await service.DeleteSavedMeasurementsAsync(ct));
        release.Release();
        await probe;

        Assert.True(await service.DeleteSavedMeasurementsAsync(ct));
        Assert.Equal(new SavedFilesSize(0, 0, null), service.SavedDataSize().Measurements);
    }

    /// <summary>One cached file can be deleted, but not while a probe runs, and not one the cache doesn't list.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CacheFileIsDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixtures = Path.Combine(_directory, "fixtures");
        Directory.CreateDirectory(Path.Combine(fixtures, "samples"));
        await File.WriteAllBytesAsync(Path.Combine(fixtures, "samples", "a.mkv"), new byte[10], ct);
        await File.WriteAllBytesAsync(Path.Combine(fixtures, "samples", "b.mkv"), new byte[10], ct);
        using var release = new SemaphoreSlim(0);
        using var service = CreateBlocked(release, fixtures);

        var probe = service.RunAsync(ct);
        Assert.Equal(DeleteOutcome.Busy, await service.DeleteCacheFileAsync("samples", "a.mkv", ct));
        release.Release();
        await probe;

        Assert.Equal(DeleteOutcome.NotFound, await service.DeleteCacheFileAsync("samples", "c.mkv", ct));
        Assert.Equal(DeleteOutcome.Deleted, await service.DeleteCacheFileAsync("samples", "a.mkv", ct));
        Assert.Equal(["b.mkv"], service.FixtureCacheContents().Select(e => e.File));
    }

    /// <summary>Deleting all data removes the report, diagnostics, speed runs, saved measurements, report cache and clips, and clears the last status; nothing goes when the settings history can't be cleared.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PurgeAllDeletesEverything()
    {
        var ct = TestContext.Current.CancellationToken;
        string[] directories = [Path.Combine(_directory, "fixtures", "samples"), Path.Combine(_directory, "results"), Path.Combine(_directory, "reports"), Path.Combine(_directory, "speed-history")];
        using var service = new ProbeService(_ => Task.FromResult(Reports.Sample()), () => false, Path.Combine(_directory, "latest.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance)
        {
            FixturesDirectory = Path.Combine(_directory, "fixtures"),
            SpeedResultsDirectory = directories[1],
            ReportCacheDirectory = directories[2],
        };
        Assert.Equal(ProbeRunResult.Completed, await service.RunAsync(ct));
        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "x.json"), "{}", ct);
        }

        await File.WriteAllTextAsync(service.DiagnosticsPath, "zip", ct);
        await File.WriteAllTextAsync(service.SpeedPath, "{}", ct);

        Assert.False(await service.PurgeAllAsync(_ => Task.FromResult(false), ct));
        Assert.True(File.Exists(service.SpeedPath));

        Assert.True(await service.PurgeAllAsync(_ => Task.FromResult(true), ct));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
        Assert.Null(await service.LatestJsonAsync(ct));
        Assert.Null(service.Status.LastCompletedUtc);
    }

    /// <summary>Disposing, as the server does when it shuts down, doesn't wait for a running probe; the probe is cancelled and finishes on its own.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task DisposeCancelsARunningProbeWithoutWaiting()
    {
        var started = new TaskCompletionSource();
        var service = Create(
            async ct =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return Reports.Sample();
            },
            transcoding: false);

        Assert.Equal(ProbeRunResult.Started, await service.StartAsync(TestContext.Current.CancellationToken));
        await started.Task;
        Assert.Equal(ProbeState.Running, service.Status.State);
        service.Dispose();

        await service.Background;
        Assert.Equal(ProbeState.Idle, service.Status.State);
        Assert.Null(service.Status.LastError);
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

    /// <summary>Reads a speed report, failing the test when there's none or it doesn't parse.</summary>
    /// <param name="json">The report's JSON, or null.</param>
    /// <returns>The report.</returns>
    private static SpeedReport SpeedReportFrom(string? json)
    {
        Assert.NotNull(json);
        var report = SpeedReportStore.Deserialize(json);
        Assert.NotNull(report);
        return report;
    }

    /// <summary>Creates a service with a scripted probe.</summary>
    /// <param name="probe">The probe.</param>
    /// <param name="transcoding">Whether a session is transcoding.</param>
    /// <returns>The service.</returns>
    private ProbeService Create(Func<CancellationToken, Task<CapabilityReport>> probe, bool transcoding) =>
        new(probe, () => transcoding, Path.Combine(_directory, "latest.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance);

    /// <summary>Creates a service over a clip cache whose probe waits for a release, for checks made while a probe runs.</summary>
    /// <param name="release">Released to let the probe finish.</param>
    /// <param name="fixtures">The clip cache directory.</param>
    /// <param name="results">The saved measurements directory, or null.</param>
    /// <returns>The service.</returns>
    private ProbeService CreateBlocked(SemaphoreSlim release, string fixtures, string? results = null) =>
        new(
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
            SpeedResultsDirectory = results,
        };
}
