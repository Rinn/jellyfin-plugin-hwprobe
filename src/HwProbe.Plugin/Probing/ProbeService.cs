using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HwProbe.Configuration;
using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.Jellyfin;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Runs one probe at a time inside the server and keeps the latest report.</summary>
public sealed partial class ProbeService : IDisposable
{
    /// <summary>The most speed runs kept in the history.</summary>
    internal const int SpeedHistoryLimit = 250;

    private readonly Func<IProgress<ProbeProgress>, CancellationToken, Task<CapabilityReport>> _probe;
    private readonly Func<bool> _isTranscoding;
    private readonly string _latestPath;
    private readonly TimeProvider _time;
    private readonly TimeSpan _settle;
    private readonly ILogger _logger;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A run still in progress releases it after Dispose; it holds no unmanaged handle.")]
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Cancelled when the server shuts down, so a background probe or speed run kills its ffmpeg instead of outliving it.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Runs still in progress read its token after Dispose cancels it; it has no timer or wait handle to free.")]
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<SpeedResult> _speedSoFar = [];

    // The latest step of a running probe; written by the probe, read by Status.
    private volatile ProbeProgress? _probeProgress;

    // Set when a run that cancels for transcodes did so, for its report.
    private volatile bool _cancelledForTranscode;

    private SpeedOptions? _speedRunning;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The speed run that owns it disposes it when it ends.")]
    private CancellationTokenSource? _speedCancel;
    private SpeedPause? _speedPause;
    private DateTimeOffset? _measuringSince;
    private ProbeStatus _status = new(ProbeState.Idle, null, null, null);
    private Task<ProbeRunResult>? _background;

    /// <summary>Initializes a new instance of the <see cref="ProbeService"/> class from server services.</summary>
    /// <param name="arguments">Argument source over the server's EncodingHelper dependencies.</param>
    /// <param name="mediaEncoder">The server's media encoder, for the ffmpeg path.</param>
    /// <param name="paths">Server paths, for caches and the saved report.</param>
    /// <param name="sessions">Sessions, to refuse probing while anything transcodes.</param>
    /// <param name="baseline">Environment values captured when the plugin loaded.</param>
    /// <param name="config">Server configuration, for the encoding settings a speed run starts from.</param>
    /// <param name="files">Describes library items for speed runs on real files.</param>
    /// <param name="logger">Logger.</param>
    public ProbeService(IArgumentSourceFactory arguments, IMediaEncoder mediaEncoder, IApplicationPaths paths, ISessionManager sessions, ServerEnvironmentBaseline baseline, IServerConfigurationManager config, LibraryFiles files, ILogger<ProbeService> logger)
        : this(
            (progress, ct) => RunEngineAsync(arguments, mediaEncoder, paths, baseline, logger, progress, ct),
            TranscodingCheck(sessions),
            LatestPath(paths),
            TimeProvider.System,
            TimeSpan.FromSeconds(2),
            logger)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(mediaEncoder);
        ArgumentNullException.ThrowIfNull(paths);
        CurrentFfmpeg = () => (mediaEncoder.EncoderPath, mediaEncoder.EncoderVersion);
        MeasureSpeed = (speed, backends, progress, ct) => RunSpeedEngineAsync(arguments, mediaEncoder, paths, baseline, speed, backends, progress, ct);
        ServerSpeedSettings = () => SettingsFrom(config.GetEncodingOptions());
        ServerBackend = () => BackendFrom(config.GetEncodingOptions());
        FindFile = files.Find;
        FixturesDirectory = ServerEngineOptions(mediaEncoder, paths).FixturesDirectory;
        SpeedResultsDirectory = SpeedEngine.ResultCacheFor(ServerEngineOptions(mediaEncoder, paths)).Directory;
        ReportCacheDirectory = ServerEngineOptions(mediaEncoder, paths).ReportCacheDirectory;
        LogDirectory = paths.LogDirectoryPath;
    }

    /// <summary>Initializes a new instance of the <see cref="ProbeService"/> class with injected behaviour.</summary>
    /// <param name="probe">Runs one probe.</param>
    /// <param name="isTranscoding">Reports whether any session is transcoding.</param>
    /// <param name="latestPath">Where the latest report is saved.</param>
    /// <param name="time">Clock for status timestamps.</param>
    /// <param name="settle">How long to wait before checking for a transcode a second time.</param>
    /// <param name="logger">Logger.</param>
    internal ProbeService(Func<CancellationToken, Task<CapabilityReport>> probe, Func<bool> isTranscoding, string latestPath, TimeProvider time, TimeSpan settle, ILogger logger)
        : this((_, ct) => probe(ct), isTranscoding, latestPath, time, settle, logger)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ProbeService"/> class with a probe that reports its progress.</summary>
    /// <param name="probe">Runs one probe, reporting each step.</param>
    /// <param name="isTranscoding">Reports whether any session is transcoding.</param>
    /// <param name="latestPath">Where the latest report is saved.</param>
    /// <param name="time">Clock for status timestamps.</param>
    /// <param name="settle">How long to wait before checking for a transcode a second time.</param>
    /// <param name="logger">Logger.</param>
    internal ProbeService(Func<IProgress<ProbeProgress>, CancellationToken, Task<CapabilityReport>> probe, Func<bool> isTranscoding, string latestPath, TimeProvider time, TimeSpan settle, ILogger logger)
    {
        _probe = probe;
        _isTranscoding = isTranscoding;
        _latestPath = latestPath;
        _time = time;
        _settle = settle;
        _logger = logger;
    }

    /// <summary>Gets the current status.</summary>
    public ProbeStatus Status
    {
        get
        {
            lock (_speedSoFar)
            {
                if (_speedRunning is null || _speedPause is not { } pause)
                {
                    return _status.State == ProbeState.Running && _status.Activity == ProbeActivity.Probe && _probeProgress is { } p
                        ? _status with { Done = p.Done, Total = p.Total == 0 ? null : p.Total, Step = p.Step, ElapsedSeconds = _status.LastStartedUtc is { } probeStarted ? (int)(_time.GetUtcNow() - probeStarted).TotalSeconds : null }
                        : _status;
                }

                var phase = _speedCancel?.IsCancellationRequested == true ? SpeedPhase.Cancelling
                    : pause.IsHolding ? SpeedPhase.Deferring
                    : pause.IsPaused ? (pause.IsWaiting ? SpeedPhase.Paused : SpeedPhase.Pausing)
                    : _measuringSince is null ? SpeedPhase.Preparing
                    : SpeedPhase.Measuring;
                var now = _time.GetUtcNow();
                return _status with
                {
                    Phase = phase,
                    ElapsedSeconds = _status.LastStartedUtc is { } started ? (int)(now - started).TotalSeconds : null,
                };
            }
        }
    }

    /// <summary>Gets where the latest probe's diagnostics zip is saved.</summary>
    internal string DiagnosticsPath => DiagnosticsPathFor(_latestPath);

    /// <summary>Gets where the latest speed report is saved.</summary>
    internal string SpeedPath => Path.Combine(Path.GetDirectoryName(_latestPath)!, "speed.json");

    /// <summary>Gets where every speed run is kept.</summary>
    internal string SpeedHistoryDirectory => Path.Combine(Path.GetDirectoryName(_latestPath)!, "speed-history");

    /// <summary>Gets the speed run, or null when this service can't measure speed.</summary>
    internal Func<SpeedOptions, IReadOnlyCollection<(HwType Type, string Device)>, IProgress<SpeedProgress>, CancellationToken, Task<SpeedReport>>? MeasureSpeed { get; init; }

    /// <summary>Gets where probes and speed runs cache their clips, or null when unknown.</summary>
    internal string? FixturesDirectory { get; init; }

    /// <summary>Gets where measurements are saved for reuse, or null when unset.</summary>
    internal string? SpeedResultsDirectory { get; init; }

    /// <summary>Gets Jellyfin's log folder, whose HwProbe entries the diagnostics zip includes, or null.</summary>
    internal string? LogDirectory { get; init; }

    /// <summary>Gets where probe reports are cached by fingerprint, or null when this service doesn't probe the server.</summary>
    internal string? ReportCacheDirectory { get; init; }

    /// <summary>Gets the lookup from a library item to its file.</summary>
    internal Func<Guid, SpeedFile?> FindFile { get; init; } = _ => null;

    /// <summary>Gets the server's encoding settings, as a speed run starts from them.</summary>
    internal Func<SpeedSettings> ServerSpeedSettings { get; init; } = () => new SpeedSettings();

    /// <summary>Gets the server's configured backend and device, for suggestions.</summary>
    internal Func<(HwType Type, string Device)> ServerBackend { get; init; } = () => (HwType.none, string.Empty);

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
    /// <param name="cancellationToken">Cancels the busy check; the probe itself runs to completion, or until the server shuts down.</param>
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

        _background = Task.Run(() => RunHeldAsync(_shutdown.Token), CancellationToken.None);
        return ProbeRunResult.Started;
    }

    /// <summary>Starts a speed run in the background over the backends the latest report found working.</summary>
    /// <param name="request">What to measure.</param>
    /// <param name="cancellationToken">Cancels the checks; the run itself goes to completion.</param>
    /// <returns><see cref="ProbeRunResult.Started"/>, or why it can't start.</returns>
    public Task<ProbeRunResult> StartSpeedAsync(SpeedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StartRunsAsync([(null, null, request, 0)], request.WhenTranscoding, cancellationToken);
    }

    /// <summary>Starts a test suite in the background: its steps run one after another on the backends it names.</summary>
    /// <param name="request">The suite and how to run it.</param>
    /// <param name="cancellationToken">Cancels the checks; the suite itself goes to completion.</param>
    /// <returns><see cref="ProbeRunResult.Started"/>, or why it can't start.</returns>
    public async Task<ProbeRunResult> StartSuiteAsync(SuiteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Catalog.Default.Suites.FirstOrDefault(s => s.Key == request.Key) is not { } suite)
        {
            return ProbeRunResult.Invalid;
        }

        if (await LatestJsonAsync(cancellationToken) is not { } json || ReportStore.Deserialize(json) is not { } report)
        {
            return ProbeRunResult.NoReport;
        }

        var configured = PreferredBackend(report);
        if (!SpeedSuites.Offered(suite, report, configured))
        {
            return ProbeRunResult.Invalid;
        }

        var backends = SpeedSuites.Backends(suite, configured).Select(b => b.ToString()).ToList();
        var runs = SpeedSuites.Steps(suite, Environment.ProcessorCount, ServerSpeedSettings(), configured)
            .Select(step =>
            {
                List<string> stepBackends = [.. backends.Where(b => !step.HardwareOnly || b != nameof(HwType.none))];
                return ((string?)suite.Name, (string?)step.Label, new SpeedRequest(Catalog.Default.SuiteMethod.ToString(), step.Videos, step.Outputs)
                {
                    Backends = stepBackends,
                    Options = step.Options,
                    MeasureResources = request.MeasureResources,
                    WhenTranscoding = request.WhenTranscoding,
                    ReuseResults = request.ReuseResults,
                }, step.Videos.Count * step.Outputs.Count * stepBackends.Count);
            })
            .ToList();
        return await StartRunsAsync(runs, request.WhenTranscoding, cancellationToken);
    }

    /// <summary>Lists the catalog's test suites as this server would run them.</summary>
    /// <param name="cancellationToken">Cancels reading the latest report.</param>
    /// <returns>Every suite, with its steps and whether it's offered.</returns>
    public async Task<IReadOnlyList<SuiteInfo>> SuitesAsync(CancellationToken cancellationToken)
    {
        var report = await LatestJsonAsync(cancellationToken) is { } json ? ReportStore.Deserialize(json) : null;
        var configured = report is null ? ServerBackend().Type : PreferredBackend(report);
        return [.. Catalog.Default.Suites.Select(s =>
        {
            var steps = SpeedSuites.Steps(s, Environment.ProcessorCount, ServerSpeedSettings(), configured);
            var backends = SpeedSuites.Backends(s, configured);
            return new SuiteInfo(
                s.Key,
                s.Name,
                s.Description,
                [.. steps.Select(step => step.Label)],
                backends,
                report is not null && SpeedSuites.Offered(s, report, configured),
                steps.Sum(step => step.Videos.Count * step.Outputs.Count * backends.Count(b => !step.HardwareOnly || b != HwType.none)),
                Catalog.Default.SuiteMethod,
                s.Note);
        })];
    }

    /// <summary>Pauses or resumes the running speed run; a pause takes effect when the current measurement finishes.</summary>
    /// <param name="paused">True to pause, false to resume.</param>
    /// <returns>Whether a speed run was running.</returns>
    public bool PauseSpeed(bool paused)
    {
        lock (_speedSoFar)
        {
            if (_speedRunning is null || _speedPause is not { } pause)
            {
                return false;
            }

            if (paused)
            {
                pause.Pause();
            }
            else
            {
                pause.Resume();
            }

            return true;
        }
    }

    /// <summary>Cancels the running speed run; it stops its ffmpeg runs and keeps the measurements already finished.</summary>
    /// <returns>Whether a speed run was running.</returns>
    public bool CancelSpeed()
    {
        lock (_speedSoFar)
        {
            if (_speedRunning is null || _speedCancel is not { } cancel)
            {
                return false;
            }

            cancel.Cancel();
            return true;
        }
    }

    /// <summary>Returns the running speed run's results so far as a speed report, so the page fills in as they finish.</summary>
    /// <returns>The JSON, or null when no speed run is running.</returns>
    public string? RunningSpeedJson()
    {
        lock (_speedSoFar)
        {
            return _speedRunning is not { } speed ? null : SpeedReportStore.Serialize(new SpeedReport(_time.GetUtcNow(), new FfmpegSummary(string.Empty, "Server", "unknown", true), speed.Method, [.. _speedSoFar])
            {
                Settings = speed.Settings,
                Repeats = speed.Repeats,
            });
        }
    }

    /// <summary>Returns the latest speed report as JSON, unless it's from another HwProbe version or ffmpeg.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The JSON, or null when none was measured with this HwProbe and ffmpeg.</returns>
    public async Task<string?> LatestSpeedJsonAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SpeedPath))
        {
            return null;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(SpeedPath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        var speed = SpeedReportStore.Deserialize(json);
        return speed?.HwProbeVersion == CapabilityReport.CurrentHwProbeVersion && IsCurrentFfmpeg(speed.Ffmpeg) ? json : null;
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

    /// <summary>Returns the latest probe's diagnostics zip, if it was made by the probe that wrote the report shown.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The zip, or null when there's none, <see cref="LatestJsonAsync"/> returns null, or the zip is from another probe.</returns>
    /// <remarks>The zip and report are saved separately, so either can be left from an earlier probe when the other fails to save.</remarks>
    public async Task<byte[]?> LatestDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(DiagnosticsPath) || await LatestJsonAsync(cancellationToken) is not { } json)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(DiagnosticsPath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        var latest = ReportStore.Deserialize(json);
        var bundled = await DiagnosticsBundle.ReadReportAsync(bytes, cancellationToken);
        if (bundled is null || bundled.GeneratedUtc != latest?.GeneratedUtc)
        {
            return null;
        }

        // Read when downloaded, so it holds what happened since the probe too.
        return await DiagnosticsBundle.WithFileAsync(bytes, "jellyfin.log", await PluginLog.ReadAsync(LogDirectory, cancellationToken), cancellationToken);
    }

    /// <summary>Lists the saved speed runs, newest first.</summary>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The runs; unreadable files are left out.</returns>
    public async Task<IReadOnlyList<SpeedHistoryEntry>> SpeedHistoryAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(SpeedHistoryDirectory))
        {
            return [];
        }

        List<SpeedHistoryEntry> entries = [];
        foreach (var file in Directory.EnumerateFiles(SpeedHistoryDirectory, "*.json").Order(StringComparer.Ordinal).Reverse())
        {
            string json;
            try
            {
                json = await File.ReadAllTextAsync(file, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                // A run finishing now deletes the oldest file.
                continue;
            }

            if (SpeedReportStore.Deserialize(json) is { } report)
            {
                var current = report.HwProbeVersion == CapabilityReport.CurrentHwProbeVersion && IsCurrentFfmpeg(report.Ffmpeg);
                entries.Add(new SpeedHistoryEntry(Path.GetFileNameWithoutExtension(file), report.GeneratedUtc, report.Method.ToString(), report.Results.Select(r => r.Test).Distinct(StringComparer.Ordinal).Count(), current) { Suite = report.Suite, SuiteStep = report.SuiteStep, SuiteStartedUtc = report.SuiteStartedUtc });
            }
        }

        return entries;
    }

    /// <summary>Returns one saved speed run as JSON.</summary>
    /// <param name="id">The run, as <see cref="SpeedHistoryAsync"/> lists it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The JSON, or null for an unknown run.</returns>
    public async Task<string?> SpeedHistoryJsonAsync(string id, CancellationToken cancellationToken)
    {
        // Only names this service writes, so the ID can't reach outside the folder.
        if (string.IsNullOrEmpty(id) || !HistoryId().IsMatch(id))
        {
            return null;
        }

        var path = Directory.Exists(SpeedHistoryDirectory)
            ? Directory.EnumerateFiles(SpeedHistoryDirectory, "*.json").FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == id)
            : null;
        return path is null ? null : await File.ReadAllTextAsync(path, cancellationToken);
    }

    /// <summary>Deletes one saved speed run, or every one; the latest run shown goes with the newest.</summary>
    /// <param name="id">The run, as <see cref="SpeedHistoryAsync"/> lists it, or null for every run.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The outcome.</returns>
    public async Task<DeleteOutcome> DeleteSpeedHistoryAsync(string? id, CancellationToken cancellationToken)
    {
        // Only names this service writes, so the ID can't reach outside the folder.
        if (id is not null && !HistoryId().IsMatch(id))
        {
            return DeleteOutcome.NotFound;
        }

        // A run saves its report when it finishes; deleting meanwhile could leave speed.json pointing at nothing.
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return DeleteOutcome.Busy;
        }

        try
        {
            List<string> files = Directory.Exists(SpeedHistoryDirectory)
                ? [.. Directory.EnumerateFiles(SpeedHistoryDirectory, "*.json").Order(StringComparer.Ordinal)]
                : [];
            var doomed = files.Where(f => id is null || Path.GetFileNameWithoutExtension(f) == id).ToHashSet(StringComparer.Ordinal);
            if (id is not null && doomed.Count == 0)
            {
                return DeleteOutcome.NotFound;
            }

            var newest = files.Count > 0 ? files[^1] : null;
            foreach (var file in doomed)
            {
                File.Delete(file);
            }

            if (newest is not null && doomed.Contains(newest))
            {
                var next = files.LastOrDefault(f => !doomed.Contains(f));
                if (next is null)
                {
                    File.Delete(SpeedPath);
                }
                else
                {
                    File.Copy(next, SpeedPath, overwrite: true);
                }
            }

            return DeleteOutcome.Deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns the size of the test clips and samples cached for probes and speed runs.</summary>
    /// <returns>Bytes and files; zero when nothing is cached.</returns>
    public CacheSize FixtureCacheSize()
    {
        if (FixturesDirectory is not { } directory || !Directory.Exists(directory))
        {
            return new CacheSize(0, 0);
        }

        var files = new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        return new CacheSize(files.Sum(f => f.Length), files.Count);
    }

    /// <summary>Draws suggestions from a run and every saved run this version and ffmpeg made.</summary>
    /// <param name="id">The run shown, as <see cref="SpeedHistoryAsync"/> lists it, or null for the latest.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The suggestions; empty when there's no such run.</returns>
    public async Task<IReadOnlyList<SpeedSuggestion>> SpeedSuggestionsAsync(string? id, CancellationToken cancellationToken)
    {
        var shownJson = id is null ? await LatestSpeedJsonAsync(cancellationToken) : await SpeedHistoryJsonAsync(id, cancellationToken);
        if (shownJson is null || SpeedReportStore.Deserialize(shownJson) is not { } shown)
        {
            return [];
        }

        // Runs from another version or ffmpeg aren't compared: their figures differ for reasons no setting explains.
        List<SpeedReport> runs = [];
        foreach (var entry in (await SpeedHistoryAsync(cancellationToken)).Where(h => h.Current))
        {
            if (await SpeedHistoryJsonAsync(entry.Id, cancellationToken) is { } json && SpeedReportStore.Deserialize(json) is { } run)
            {
                runs.Add(run);
            }
        }

        var (type, device) = ServerBackend();
        return SpeedAdvisor.Advise(shown, runs, type, device, ServerSpeedSettings());
    }

    /// <summary>Lists the cached clips, samples and downloads.</summary>
    /// <returns>The entries; empty when nothing is cached.</returns>
    public IReadOnlyList<CacheEntry> FixtureCacheContents() => Core.Fixtures.FixtureCacheContents.List(FixturesDirectory);

    /// <summary>Deletes the cached clips and samples; the next probe or speed run makes or downloads them again.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>False when a probe or speed run is using them.</returns>
    public async Task<bool> PurgeFixtureCacheAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            if (FixturesDirectory is { } directory && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            // Saved measurements go with the clips they were measured on.
            if (SpeedResultsDirectory is { } results && Directory.Exists(results))
            {
                Directory.Delete(results, recursive: true);
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deletes one cached clip, sample or download; the next probe or speed run makes or downloads it again.</summary>
    /// <param name="folder">The entry's folder, as <see cref="FixtureCacheContents"/> lists it.</param>
    /// <param name="file">The entry's file name.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The outcome; NotFound for a file the cache doesn't list.</returns>
    public async Task<DeleteOutcome> DeleteCacheFileAsync(string folder, string file, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return DeleteOutcome.Busy;
        }

        try
        {
            return Core.Fixtures.FixtureCacheContents.Delete(FixturesDirectory, folder, file) ? DeleteOutcome.Deleted : DeleteOutcome.NotFound;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deletes everything probes and speed runs saved: the latest report and diagnostics, every speed run, saved measurements, the report cache, and the cached clips.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>False when a probe or speed run is running.</returns>
    public async Task<bool> PurgeAllAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            foreach (var file in new[] { _latestPath, DiagnosticsPath, SpeedPath })
            {
                File.Delete(file);
            }

            foreach (var directory in new[] { SpeedHistoryDirectory, FixturesDirectory, SpeedResultsDirectory, ReportCacheDirectory })
            {
                if (directory is not null && Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }

            _status = new(ProbeState.Idle, null, null, null);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Doesn't wait: a running probe or speed run sees the cancellation, kills its ffmpeg tree and releases the gate on
    /// its own. The gate and the speed run's token source are left to it, as disposing them here would make that release
    /// throw; neither holds an unmanaged handle.
    /// </remarks>
    public void Dispose() => _shutdown.Cancel();

    /// <summary>Reports whether a session's stream re-encodes video or audio.</summary>
    /// <param name="info">The session's transcoding info, null when it plays directly.</param>
    /// <returns>False for direct play and a remux (both streams copied); true otherwise.</returns>
    internal static bool IsTranscoding(TranscodingInfo? info) => info is not null && (!info.IsVideoDirect || !info.IsAudioDirect);

    /// <summary>Parses a request from the page.</summary>
    /// <param name="request">The request.</param>
    /// <param name="file">The library item's file, or null.</param>
    /// <returns>The options with default settings, or null when a name is unknown.</returns>
    private static SpeedOptions? ParseSpeed(SpeedRequest request, SpeedFile? file)
    {
        if (!Enum.TryParse<SpeedMethod>(request.Method, ignoreCase: true, out var method) || !Enum.IsDefined(method))
        {
            return null;
        }

        var videos = request.Videos.Count == 0 ? SpeedCatalog.DefaultVideos : request.Videos;
        var outputs = request.Outputs.Count == 0 ? SpeedCatalog.DefaultOutputs : request.Outputs;
        if (videos.Any(v => SpeedCatalog.FindVideo(v) is null && !(v == SpeedCatalog.LibraryKey && file is not null))
            || outputs.Any(o => SpeedCatalog.FindOutput(o) is null))
        {
            return null;
        }

        if (!Catalog.Default.Repeats.Any(o => o.Value == request.Repeats) || !Catalog.Default.TimeLimits.Any(o => o.Value == request.TimeLimitSeconds)
            || request.Options?.Any(o => Catalog.Default.Options.FirstOrDefault(c => c.Key == o.Key) is not { } option || !option.Takes(o.Value)) == true
            || request.Backends?.Any(b => !Enum.TryParse<HwType>(b, out var type) || !Enum.IsDefined(type)) == true
            || request.Backends is [])
        {
            return null;
        }

        return new SpeedOptions(method, videos, outputs, new SpeedSettings())
        {
            File = file,
            Backends = request.Backends?.Select(Enum.Parse<HwType>).ToList(),
            Repeats = request.Repeats,
            TimeLimit = request.TimeLimitSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            ReuseResults = request.ReuseResults,
            MeasureResources = request.MeasureResources,
        };
    }

    /// <summary>Reads the speed run's starting settings from the server's encoding options.</summary>
    /// <param name="options">The server's encoding options.</param>
    /// <returns>The settings.</returns>
    private static SpeedSettings SettingsFrom(EncodingOptions options) => new()
    {
        EncoderPreset = options.EncoderPreset == EncoderPreset.auto ? null : options.EncoderPreset.ToString(),
        AudioVbr = options.EnableAudioVbr,
        H264Crf = options.H264Crf,
        H265Crf = options.H265Crf,
        LowPowerH264 = options.EnableIntelLowPowerH264HwEncoder,
        LowPowerHevc = options.EnableIntelLowPowerHevcHwEncoder,
        VppTonemap = options.EnableVppTonemapping,
        PreferNativeDecoder = options.PreferSystemNativeHwDecoder,
        EnhancedNvdec = options.EnableEnhancedNvdecDecoder,
        DoubleRate = options.DeinterlaceDoubleRate,
        Bwdif = options.DeinterlaceMethod == DeinterlaceMethod.bwdif,
        Tonemap = options.EnableTonemapping,
        EncodingThreadCount = options.EncodingThreadCount,
        VideoToolboxTonemap = options.EnableVideoToolboxTonemapping,
        TonemapAlgorithm = options.TonemappingAlgorithm.ToString(),
        TonemapMode = options.TonemappingMode.ToString(),
        TonemapRange = options.TonemappingRange.ToString(),
        TonemapDesat = options.TonemappingDesat,
        TonemapPeak = options.TonemappingPeak,
        TonemapParam = options.TonemappingParam,
        DownmixAlgorithm = options.DownMixStereoAlgorithm.ToString(),
        DownmixBoost = options.DownMixAudioBoost,
    };

    /// <summary>Reads the configured backend and its device.</summary>
    /// <param name="options">The server's encoding options.</param>
    /// <returns>The backend; the device is empty for backends that don't take one.</returns>
    private static (HwType Type, string Device) BackendFrom(EncodingOptions options) => options.HardwareAccelerationType switch
    {
        HardwareAccelerationType.vaapi => (HwType.vaapi, options.VaapiDevice ?? string.Empty),
        HardwareAccelerationType.qsv => (HwType.qsv, options.QsvDevice ?? string.Empty),

        // HwType mirrors HardwareAccelerationType value-for-value.
        var other => ((HwType)(int)other, string.Empty),
    };

    /// <summary>Matches a history ID: the UTC time a run finished.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^\d{8}T\d{6}Z$")]
    private static partial Regex HistoryId();

    /// <summary>Returns a check for any session that is transcoding.</summary>
    /// <param name="sessions">The session manager.</param>
    /// <returns>True while any session is transcoding.</returns>
    /// <remarks>
    /// <c>TranscodeManager.ReportTranscodingProgress</c> also sets <c>TranscodingInfo</c> for a remux, which copies both streams.
    /// </remarks>
    private static Func<bool> TranscodingCheck(ISessionManager sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return () => sessions.Sessions.Any(s => IsTranscoding(s.TranscodingInfo));
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
    /// <param name="progress">Receives each step.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The report.</returns>
    private static async Task<CapabilityReport> RunEngineAsync(IArgumentSourceFactory arguments, IMediaEncoder mediaEncoder, IApplicationPaths paths, ServerEnvironmentBaseline baseline, ILogger logger, IProgress<ProbeProgress> progress, CancellationToken cancellationToken)
    {
        var options = ServerEngineOptions(mediaEncoder, paths);
        var environment = EnvironmentRules.InServer(baseline.Values, new Dictionary<string, string>());
        var recorder = new RecordingFfmpegRunner(new FfmpegRunner());
        CapabilityReport report;
        using (var engine = new ProbeEngine(recorder, arguments, new HostPlatform(), TimeProvider.System, environment) { Progress = progress })
        {
            report = await engine.RunAsync(options, cancellationToken);
        }

        try
        {
            await DiagnosticsBundle.WriteAsync(DiagnosticsPathFor(LatestPath(paths)), report, recorder.Runs, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The report matters more than its logs.
            Log.DiagnosticsFailed(logger, ex);
        }

        return report;
    }

    /// <summary>Runs the speed engine against the server's ffmpeg.</summary>
    /// <param name="arguments">Argument source factory.</param>
    /// <param name="mediaEncoder">The server's media encoder.</param>
    /// <param name="paths">Server paths.</param>
    /// <param name="baseline">Environment values captured when the plugin loaded.</param>
    /// <param name="speed">What to measure.</param>
    /// <param name="backends">The working backends.</param>
    /// <param name="progress">Receives measurements done and the total.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The speed report.</returns>
    private static async Task<SpeedReport> RunSpeedEngineAsync(IArgumentSourceFactory arguments, IMediaEncoder mediaEncoder, IApplicationPaths paths, ServerEnvironmentBaseline baseline, SpeedOptions speed, IReadOnlyCollection<(HwType Type, string Device)> backends, IProgress<SpeedProgress> progress, CancellationToken cancellationToken)
    {
        var environment = EnvironmentRules.InServer(baseline.Values, new Dictionary<string, string>());
        using var engine = new SpeedEngine(new FfmpegRunner(), arguments, new HostPlatform(), TimeProvider.System, environment);
        return await engine.RunAsync(ServerEngineOptions(mediaEncoder, paths), speed, backends, progress, cancellationToken);
    }

    /// <summary>Returns the engine options for the server's ffmpeg and caches.</summary>
    /// <param name="mediaEncoder">The server's media encoder.</param>
    /// <param name="paths">Server paths.</param>
    /// <returns>The options.</returns>
    private static EngineOptions ServerEngineOptions(IMediaEncoder mediaEncoder, IApplicationPaths paths)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var root = Path.Combine(paths.CachePath, "hwprobe");
        return new EngineOptions(
            new FfmpegLocation(mediaEncoder.EncoderPath, FfmpegSource.Server),
            StopStage.Matrix,
            new HashSet<HwType>(),
            null,
            TimeSpan.FromSeconds(config.ProbeTimeoutSeconds),
            TimeSpan.FromSeconds(config.FixtureTimeoutSeconds),
            Path.Combine(root, "fixtures"),
            Path.Combine(root, "reports"),
            Refresh: true);
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
        _status = _status with { State = ProbeState.Running, Activity = ProbeActivity.Probe, LastStartedUtc = _time.GetUtcNow(), LastError = null };
        try
        {
            var report = await _probe(new SynchronousProgress<ProbeProgress>(p => _probeProgress = p), cancellationToken);
            await ReportStore.WriteAsync(report, _latestPath, cancellationToken);
            var viable = report.Backends.Count(b => b.Verdict == BackendVerdict.Viable);
            Log.Completed(_logger, viable);
            return ProbeRunResult.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ProbeRunResult.Failed;
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
            _probeProgress = null;
            _status = _status with { State = ProbeState.Idle, LastCompletedUtc = _time.GetUtcNow() };
            _gate.Release();
        }
    }

    /// <summary>Cancels the speed run when the server starts transcoding, until the run ends.</summary>
    /// <param name="run">The run's cancellation source.</param>
    /// <returns>A task that ends with the run.</returns>
    private async Task WatchForTranscodeAsync(CancellationTokenSource run)
    {
        var token = run.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _time, token);
                if (_isTranscoding())
                {
                    _cancelledForTranscode = true;
                    await run.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The run ended or was cancelled.
        }
        catch (ObjectDisposedException)
        {
            // The run ended and its source was disposed.
        }
    }

    /// <summary>Keeps a speed run in the history, dropping the oldest beyond <see cref="SpeedHistoryLimit"/>.</summary>
    /// <param name="report">The run.</param>
    /// <returns>A task that completes when it's saved.</returns>
    private async Task SaveSpeedHistoryAsync(SpeedReport report)
    {
        var id = report.GeneratedUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        await SpeedReportStore.WriteAsync(report, Path.Combine(SpeedHistoryDirectory, id + ".json"), CancellationToken.None);
        foreach (var old in Directory.EnumerateFiles(SpeedHistoryDirectory, "*.json").Order(StringComparer.Ordinal).Reverse().Skip(SpeedHistoryLimit))
        {
            File.Delete(old);
        }
    }

    /// <summary>Returns the hardware backend suites run on: the configured one, or QSV in place of VAAPI when it works on the same GPU.</summary>
    /// <param name="report">The latest probe.</param>
    /// <returns>The backend type; <see cref="HwType.none"/> for software.</returns>
    private HwType PreferredBackend(CapabilityReport report) =>
        BackendPreference.Prefer(ServerBackend(), report.Backends.Where(b => b.Verdict == BackendVerdict.Viable).Select(b => (b.Type, b.Device))).Type;

    /// <summary>Checks speed runs and starts them one after another in the background.</summary>
    /// <param name="requests">The runs, with the suite and step each belongs to and, in a suite, its expected number of measurements.</param>
    /// <param name="whenTranscoding">What the runs do when the server transcodes.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns><see cref="ProbeRunResult.Started"/>, or why they can't start.</returns>
    private async Task<ProbeRunResult> StartRunsAsync(IReadOnlyList<(string? Suite, string? Step, SpeedRequest Request, int Estimate)> requests, TranscodeAction whenTranscoding, CancellationToken cancellationToken)
    {
        List<(string?, string?, SpeedOptions, SpeedRequest, int)> parsed = [];
        foreach (var (suite, step, request, estimate) in requests)
        {
            var file = request.ItemId is { } item ? FindFile(item) : null;
            if (MeasureSpeed is null || (request.ItemId is not null && file is null) || ParseSpeed(request, file) is not { } speed)
            {
                return ProbeRunResult.Invalid;
            }

            parsed.Add((suite, step, speed, request, estimate));
        }

        if (MeasureSpeed is not { } measure)
        {
            return ProbeRunResult.Invalid;
        }

        if (await LatestJsonAsync(cancellationToken) is not { } json || ReportStore.Deserialize(json) is not { } report)
        {
            return ProbeRunResult.NoReport;
        }

        // A run that defers to transcodes starts anyway and waits for the transcode to end.
        if (whenTranscoding != TranscodeAction.Pause && await IsBusyAsync(cancellationToken))
        {
            return ProbeRunResult.ServerBusy;
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return ProbeRunResult.AlreadyRunning;
        }

        List<(HwType, string)> backends = [.. report.Backends.Where(b => b.Verdict == BackendVerdict.Viable).Select(b => (b.Type, b.Device))];
        _speedCancel = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _speedPause = new SpeedPause(_time) { Busy = whenTranscoding == TranscodeAction.Pause ? _isTranscoding : null };
        _cancelledForTranscode = false;
        if (whenTranscoding == TranscodeAction.Cancel)
        {
            _ = WatchForTranscodeAsync(_speedCancel);
        }

        var runs = parsed.Select(p =>
        {
            var (suite, step, speed, request, estimate) = p;
            var settings = ServerSpeedSettings();
            foreach (var (key, value) in request.Options ?? new Dictionary<string, string>())
            {
                settings = SpeedSettingsOptions.Apply(settings, key, value) ?? settings;
            }

            return (suite, step, speed.ForReport(report) with { Settings = settings, Pause = _speedPause }, estimate);
        }).ToList();
        _background = Task.Run(() => RunSpeedHeldAsync(measure, runs, backends, _speedCancel.Token), CancellationToken.None);
        return ProbeRunResult.Started;
    }

    /// <summary>Runs speed measurements one after another, a test suite's steps or a single run; the caller already holds the gate, which this releases.</summary>
    /// <param name="measure">The speed run.</param>
    /// <param name="runs">What to measure, with the suite and step each run belongs to (null for a run on its own) and, in a suite, its expected number of measurements.</param>
    /// <param name="backends">The working backends.</param>
    /// <param name="cancellationToken">Cancels the runs, from <see cref="CancelSpeed"/>.</param>
    /// <returns>The result.</returns>
    private async Task<ProbeRunResult> RunSpeedHeldAsync(
        Func<SpeedOptions, IReadOnlyCollection<(HwType Type, string Device)>, IProgress<SpeedProgress>, CancellationToken, Task<SpeedReport>> measure,
        List<(string? Suite, string? Step, SpeedOptions Speed, int Estimate)> runs,
        IReadOnlyCollection<(HwType Type, string Device)> backends,
        CancellationToken cancellationToken)
    {
        var started = _time.GetUtcNow();
        _status = _status with { State = ProbeState.Running, Activity = ProbeActivity.Speed, LastStartedUtc = started, LastError = null, Done = 0, Total = null, Preparing = null, Suite = null, SuiteDone = null, SuiteTotal = null, ClipsDone = null, ClipsTotal = null };

        // Each step's planned count replaces its estimate once the engine plans it.
        var totals = runs.Select(r => r.Estimate).ToList();
        var doneBefore = 0;
        try
        {
            for (var i = 0; i < runs.Count && !cancellationToken.IsCancellationRequested; i++)
            {
                var (suite, step, speed, _) = runs[i];
                var index = i;
                _status = _status with
                {
                    Suite = suite is null ? null : string.Create(CultureInfo.InvariantCulture, $"{suite}: {step}, {i + 1} of {runs.Count}"),
                    SuiteDone = suite is null ? null : doneBefore,
                    SuiteTotal = suite is null ? null : totals.Sum(),
                };
                lock (_speedSoFar)
                {
                    _speedSoFar.Clear();
                    _speedRunning = speed;
                    _measuringSince = null;
                }

                var progress = new SynchronousProgress<SpeedProgress>(p =>
                {
                    lock (_speedSoFar)
                    {
                        if (p.Planned is { } planned)
                        {
                            _speedSoFar.Clear();
                            _speedSoFar.AddRange(planned);
                        }
                        else if (p.Result is { } result)
                        {
                            // Results replace their planned row, so the table keeps its shape as it fills in.
                            var index = _speedSoFar.FindIndex(x => x.Pending && x.Type == result.Type && x.Device == result.Device && x.Test == result.Test && x.Variant == result.Variant);
                            if (index >= 0)
                            {
                                _speedSoFar[index] = result;
                            }
                            else
                            {
                                _speedSoFar.Add(result);
                            }
                        }
                        else if (p.Preparing is null)
                        {
                            _measuringSince ??= _time.GetUtcNow();
                        }
                    }

                    if (suite is not null && p.Total is { } stepTotal)
                    {
                        totals[index] = stepTotal;
                    }

                    _status = _status with { Done = p.Done, Total = p.Total, Preparing = p.Preparing, SuiteTotal = suite is null ? null : totals.Sum(), ClipsDone = p.ClipsDone, ClipsTotal = p.ClipsTotal };
                });
                var report = await measure(speed, backends, progress, cancellationToken);
                report = report with { CancelledForTranscode = report.Cancelled && _cancelledForTranscode, Suite = suite, SuiteStep = step, SuiteStartedUtc = suite is null ? null : started };
                if (report.Cancelled)
                {
                    Log.SpeedCancelled(_logger, report.Results.Count);
                    if (report.Results.Count == 0)
                    {
                        return ProbeRunResult.Completed;
                    }
                }
                else
                {
                    Log.SpeedCompleted(_logger, report.Results.Count);
                }

                await SpeedReportStore.WriteAsync(report, SpeedPath, CancellationToken.None);
                await SaveSpeedHistoryAsync(report);
                doneBefore += totals[i];
            }

            return ProbeRunResult.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled before measuring started, while checking ffmpeg.
            return ProbeRunResult.Completed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // As with a probe: the reason goes to the status and the log, and the server keeps running.
            Log.Failed(_logger, ex);
            _status = _status with { LastError = ex.Message };
            return ProbeRunResult.Failed;
        }
        finally
        {
            lock (_speedSoFar)
            {
                _speedRunning = null;
                _speedSoFar.Clear();
                _speedCancel?.Dispose();
                _speedCancel = null;
                _speedPause = null;
                _measuringSince = null;
            }

            _status = _status with { State = ProbeState.Idle, LastCompletedUtc = _time.GetUtcNow(), Done = null, Total = null, Preparing = null, Suite = null, SuiteDone = null, SuiteTotal = null, ClipsDone = null, ClipsTotal = null };
            _gate.Release();
        }
    }
}
