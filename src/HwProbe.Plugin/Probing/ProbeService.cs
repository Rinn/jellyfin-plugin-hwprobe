using Jellyfin.Plugin.HwProbe.Configuration;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Jellyfin;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Runs one probe at a time inside the server and keeps the latest report.</summary>
public sealed class ProbeService : IDisposable
{
    private readonly Func<CancellationToken, Task<CapabilityReport>> _probe;
    private readonly Func<bool> _isTranscoding;
    private readonly string _latestPath;
    private readonly TimeProvider _time;
    private readonly TimeSpan _settle;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProbeStatus _status = new(ProbeState.Idle, null, null, null);
    private Task<ProbeRunResult>? _background;

    /// <summary>Initializes a new instance of the <see cref="ProbeService"/> class from server services.</summary>
    /// <param name="arguments">Argument source over the server's EncodingHelper dependencies.</param>
    /// <param name="mediaEncoder">The server's media encoder, for the ffmpeg path.</param>
    /// <param name="paths">Server paths, for caches and the saved report.</param>
    /// <param name="sessions">Sessions, to refuse probing while anything transcodes.</param>
    /// <param name="baseline">Environment values captured when the plugin loaded.</param>
    /// <param name="logger">Logger.</param>
    public ProbeService(IArgumentSourceFactory arguments, IMediaEncoder mediaEncoder, IApplicationPaths paths, ISessionManager sessions, ServerEnvironmentBaseline baseline, ILogger<ProbeService> logger)
        : this(
            ct => RunEngineAsync(arguments, mediaEncoder, paths, baseline, logger, ct),
            TranscodingCheck(sessions),
            LatestPath(paths),
            TimeProvider.System,
            TimeSpan.FromSeconds(2),
            logger)
    {
        CurrentFfmpeg = () => (mediaEncoder.EncoderPath, mediaEncoder.EncoderVersion);
    }

    /// <summary>Initializes a new instance of the <see cref="ProbeService"/> class with injected behaviour.</summary>
    /// <param name="probe">Runs one probe.</param>
    /// <param name="isTranscoding">Reports whether any session is transcoding.</param>
    /// <param name="latestPath">Where the latest report is saved.</param>
    /// <param name="time">Clock for status timestamps.</param>
    /// <param name="settle">How long to wait before checking for a transcode a second time.</param>
    /// <param name="logger">Logger.</param>
    internal ProbeService(Func<CancellationToken, Task<CapabilityReport>> probe, Func<bool> isTranscoding, string latestPath, TimeProvider time, TimeSpan settle, ILogger logger)
    {
        _probe = probe;
        _isTranscoding = isTranscoding;
        _latestPath = latestPath;
        _time = time;
        _settle = settle;
        _logger = logger;
    }

    /// <summary>Gets the current status.</summary>
    public ProbeStatus Status => _status;

    /// <summary>Gets where the latest probe's diagnostics zip is saved.</summary>
    internal string DiagnosticsPath => DiagnosticsPathFor(_latestPath);

    /// <summary>Gets the ffmpeg path and version the server uses now, or null not to compare them.</summary>
    internal Func<(string Path, Version? Version)>? CurrentFfmpeg { get; init; }

    /// <summary>Gets the probe started by <see cref="StartAsync"/>, or a completed task when none was.</summary>
    internal Task Background => _background ?? Task.CompletedTask;

    /// <summary>Runs a probe now and waits for it.</summary>
    /// <param name="cancellationToken">Cancels the probe; running ffmpeg trees are killed.</param>
    /// <returns><see cref="ProbeRunResult.Completed"/>, or why it didn't run or failed.</returns>
    public async Task<ProbeRunResult> RunAsync(CancellationToken cancellationToken)
    {
        if (await IsBusyAsync(cancellationToken))
        {
            Log.SkippedBusy(_logger);
            return ProbeRunResult.ServerBusy;
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return ProbeRunResult.AlreadyRunning;
        }

        return await RunHeldAsync(cancellationToken);
    }

    /// <summary>Starts a probe in the background if one can run now.</summary>
    /// <param name="cancellationToken">Cancels the busy check; the probe itself runs to completion.</param>
    /// <returns><see cref="ProbeRunResult.Started"/>, or why it can't start.</returns>
    public async Task<ProbeRunResult> StartAsync(CancellationToken cancellationToken)
    {
        if (await IsBusyAsync(cancellationToken))
        {
            return ProbeRunResult.ServerBusy;
        }

        // Take the gate before returning, so two quick requests can't both be told "started".
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return ProbeRunResult.AlreadyRunning;
        }

        _background = Task.Run(() => RunHeldAsync(CancellationToken.None), CancellationToken.None);
        return ProbeRunResult.Started;
    }

    /// <summary>Returns the latest saved report as JSON, unless it's from another HwProbe version or ffmpeg.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The report JSON, or null when no probe has completed with this HwProbe and ffmpeg.</returns>
    /// <remarks>
    /// Advice and tests change between versions, and results belong to the ffmpeg that was tested. A stale file is
    /// left for the next probe to overwrite: deleting it here could race that probe and delete the new report.
    /// </remarks>
    public async Task<string?> LatestJsonAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_latestPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_latestPath, cancellationToken);
        var report = ReportStore.Deserialize(json);
        return report?.HwProbeVersion == CapabilityReport.CurrentHwProbeVersion && IsCurrentFfmpeg(report.Ffmpeg) ? json : null;
    }

    /// <summary>Returns the latest probe's diagnostics zip, unless its report isn't shown.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The file path, or null when there's no zip or <see cref="LatestJsonAsync"/> returns null.</returns>
    public async Task<string?> LatestDiagnosticsAsync(CancellationToken cancellationToken) =>
        File.Exists(DiagnosticsPath) && await LatestJsonAsync(cancellationToken) is not null ? DiagnosticsPath : null;

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

    /// <summary>Returns a check for any session that is transcoding.</summary>
    /// <param name="sessions">The session manager.</param>
    /// <returns>True while any session is transcoding.</returns>
    private static Func<bool> TranscodingCheck(ISessionManager sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return () => sessions.Sessions.Any(s => s.TranscodingInfo is not null);
    }

    /// <summary>Returns where the latest report is saved.</summary>
    /// <param name="paths">Server paths.</param>
    /// <returns>The file path.</returns>
    private static string LatestPath(IApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.DataPath, "hwprobe", "latest.json");
    }

    /// <summary>Returns where the diagnostics zip is saved, beside the report.</summary>
    /// <param name="latestPath">Where the report is saved.</param>
    /// <returns>The file path.</returns>
    private static string DiagnosticsPathFor(string latestPath) => Path.Combine(Path.GetDirectoryName(latestPath)!, "diagnostics.zip");

    /// <summary>Runs the probe engine against the server's ffmpeg, and saves a diagnostics zip of its launches.</summary>
    /// <param name="arguments">Argument source factory.</param>
    /// <param name="mediaEncoder">The server's media encoder.</param>
    /// <param name="paths">Server paths.</param>
    /// <param name="baseline">Environment values captured when the plugin loaded.</param>
    /// <param name="logger">Logs a zip that couldn't be saved.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The report.</returns>
    private static async Task<CapabilityReport> RunEngineAsync(IArgumentSourceFactory arguments, IMediaEncoder mediaEncoder, IApplicationPaths paths, ServerEnvironmentBaseline baseline, ILogger logger, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var root = Path.Combine(paths.CachePath, "hwprobe");
        var options = new EngineOptions(
            new FfmpegLocation(mediaEncoder.EncoderPath, FfmpegSource.Server),
            StopStage.Matrix,
            new HashSet<HwType>(),
            null,
            TimeSpan.FromSeconds(config.ProbeTimeoutSeconds),
            TimeSpan.FromSeconds(config.FixtureTimeoutSeconds),
            Path.Combine(root, "fixtures"),
            Path.Combine(root, "reports"),
            Refresh: true);

        var environment = EnvironmentRules.InServer(baseline.Values, new Dictionary<string, string>());
        var recorder = new RecordingFfmpegRunner(new FfmpegRunner());
        CapabilityReport report;
        using (var engine = new ProbeEngine(recorder, arguments, new HostPlatform(), TimeProvider.System, environment))
        {
            report = await engine.RunAsync(options, cancellationToken);
        }

        try
        {
            var scrubber = DiagnosticsScrubber.ForCurrentHost((root, "<cache>"), (paths.ProgramDataPath, "<data>"));
            await DiagnosticsBundle.WriteAsync(DiagnosticsPathFor(LatestPath(paths)), report, recorder.Runs, scrubber, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The report matters more than its logs.
            Log.DiagnosticsFailed(logger, ex);
        }

        return report;
    }

    /// <summary>Reports whether a report's ffmpeg is the one the server uses now.</summary>
    /// <param name="ffmpeg">The report's ffmpeg.</param>
    /// <returns>False when the path or the major, minor or patch version differs; an unreadable version counts as the same.</returns>
    /// <remarks>Jellyfin and HwProbe parse <c>-version</c> separately, so only the numbers both read are compared.</remarks>
    private bool IsCurrentFfmpeg(FfmpegSummary ffmpeg)
    {
        if (CurrentFfmpeg is null)
        {
            return true;
        }

        var (path, version) = CurrentFfmpeg();
        if (!string.Equals(ffmpeg.Path, path, StringComparison.Ordinal))
        {
            return false;
        }

        return version is null || !Version.TryParse(ffmpeg.Version, out var tested)
            || (tested.Major, tested.Minor, Math.Max(tested.Build, 0)) == (version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    /// <summary>Reports whether a session is transcoding, checking twice.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>True when either check finds a transcode.</returns>
    /// <remarks>
    /// A client that logs in again gets a new session without the transcode until ffmpeg's next progress report
    /// (about a second); the second check covers that.
    /// </remarks>
    private async Task<bool> IsBusyAsync(CancellationToken cancellationToken)
    {
        if (_isTranscoding())
        {
            return true;
        }

        await Task.Delay(_settle, _time, cancellationToken);
        return _isTranscoding();
    }

    /// <summary>Runs a probe; the caller already holds the gate, which this releases.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The result.</returns>
    private async Task<ProbeRunResult> RunHeldAsync(CancellationToken cancellationToken)
    {
        _status = _status with { State = ProbeState.Running, LastStartedUtc = _time.GetUtcNow(), LastError = null };
        try
        {
            var report = await _probe(cancellationToken);
            await ReportStore.WriteAsync(report, _latestPath, cancellationToken);
            var viable = report.Backends.Count(b => b.Verdict == BackendVerdict.Viable);
            Log.Completed(_logger, viable);
            return ProbeRunResult.Completed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed probe must not take the server down; the reason goes to the status and the log.
            Log.Failed(_logger, ex);
            _status = _status with { LastError = ex.Message };
            return ProbeRunResult.Failed;
        }
        finally
        {
            _status = _status with { State = ProbeState.Idle, LastCompletedUtc = _time.GetUtcNow() };
            _gate.Release();
        }
    }
}
