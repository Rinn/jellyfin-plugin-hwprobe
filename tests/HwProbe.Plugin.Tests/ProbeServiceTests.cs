using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.Probing;
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
                progress.Report((1, 1));
                return Task.FromResult(new SpeedReport(DateTimeOffset.UnixEpoch, Reports.Sample().Ffmpeg, speed.Method, [new SpeedResult(HwType.none, string.Empty, "1080p-h264", string.Empty, 300, 12, false, null)]));
            },
            ServerSpeedSettings = () => new SpeedSettings { EncoderPreset = "fast" },
        };
        var request = new SpeedRequest("full", [], ["Bitrate", "AudioVbr"]);

        Assert.Equal(ProbeRunResult.NoReport, await service.StartSpeedAsync(request, ct));
        await service.RunAsync(ct);
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Method = "fastest" }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Tests = ["8k-h266"] }, ct));
        Assert.Equal(ProbeRunResult.Invalid, await service.StartSpeedAsync(request with { Comparisons = ["None"] }, ct));

        Assert.Equal(ProbeRunResult.Started, await service.StartSpeedAsync(request, ct));
        await service.Background;

        Assert.Equal(Reports.Sample().Backends.Where(b => b.Verdict == BackendVerdict.Viable).Select(b => (b.Type, b.Device)), measured);
        Assert.Equal((SpeedMethod.Full, SpeedComparison.Bitrate | SpeedComparison.AudioVbr, "fast"), (asked!.Method, asked.Comparisons, asked.Settings.EncoderPreset));
        Assert.Equal(SpeedCatalog.Default, asked.Tests);
        Assert.Equal(ProbeActivity.Speed, service.Status.Activity);
        Assert.Null(service.Status.Total);
        Assert.Equal(12, SpeedReportStore.Deserialize((await service.LatestSpeedJsonAsync(ct))!)!.Results[0].Streams);
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
