using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HwProbe.Configuration;
using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
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
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Runs one probe at a time inside the server and keeps the latest report.</summary>
public sealed partial class ProbeService : IDisposable
{
    /// <summary>The most speed runs kept in the history.</summary>
    internal const int SpeedHistoryLimit = 50;

    private readonly Func<CancellationToken, Task<CapabilityReport>> _probe;
    private readonly Func<bool> _isTranscoding;
    private readonly string _latestPath;
    private readonly TimeProvider _time;
    private readonly TimeSpan _settle;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<SpeedResult> _speedSoFar = [];
    private SpeedOptions? _speedRunning;
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
            ct => RunEngineAsync(arguments, mediaEncoder, paths, baseline, logger, ct),
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
        FindFile = files.Find;
        FixturesDirectory = ServerEngineOptions(mediaEncoder, paths).FixturesDirectory;
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

    /// <summary>Gets where the latest speed report is saved.</summary>
    internal string SpeedPath => Path.Combine(Path.GetDirectoryName(_latestPath)!, "speed.json");

    /// <summary>Gets where every speed run is kept.</summary>
    internal string SpeedHistoryDirectory => Path.Combine(Path.GetDirectoryName(_latestPath)!, "speed-history");

    /// <summary>Gets the speed run, or null when this service can't measure speed.</summary>
    internal Func<SpeedOptions, IReadOnlyCollection<(HwType Type, string Device)>, IProgress<SpeedProgress>, CancellationToken, Task<SpeedReport>>? MeasureSpeed { get; init; }

    /// <summary>Gets where probes and speed runs cache their clips, or null when unknown.</summary>
    internal string? FixturesDirectory { get; init; }

    /// <summary>Gets the lookup from a library item to its file.</summary>
    internal Func<Guid, SpeedFile?> FindFile { get; init; } = _ => null;

    /// <summary>Gets the server's encoding settings, as a speed run starts from them.</summary>
    internal Func<SpeedSettings> ServerSpeedSettings { get; init; } = () => new SpeedSettings();

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

    /// <summary>Starts a speed run in the background over the backends the latest report found working.</summary>
    /// <param name="request">What to measure.</param>
    /// <param name="cancellationToken">Cancels the checks; the run itself goes to completion.</param>
    /// <returns><see cref="ProbeRunResult.Started"/>, or why it can't start.</returns>
    public async Task<ProbeRunResult> StartSpeedAsync(SpeedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var file = request.ItemId is { } item ? FindFile(item) : null;
        if (MeasureSpeed is not { } measure || (request.ItemId is not null && file is null) || ParseSpeed(request, file) is not { } speed)
        {
            return ProbeRunResult.Invalid;
        }

        if (await LatestJsonAsync(cancellationToken) is not { } json || ReportStore.Deserialize(json) is not { } report)
        {
            return ProbeRunResult.NoReport;
        }

        if (await IsBusyAsync(cancellationToken))
        {
            return ProbeRunResult.ServerBusy;
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return ProbeRunResult.AlreadyRunning;
        }

        List<(HwType, string)> backends = [.. report.Backends.Where(b => b.Verdict == BackendVerdict.Viable).Select(b => (b.Type, b.Device))];
        _background = Task.Run(() => RunSpeedHeldAsync(measure, speed with { Settings = ServerSpeedSettings() }, backends), CancellationToken.None);
        return ProbeRunResult.Started;
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
        return bundled is not null && bundled.GeneratedUtc == latest?.GeneratedUtc ? bytes : null;
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
            if (SpeedReportStore.Deserialize(await File.ReadAllTextAsync(file, cancellationToken)) is { } report)
            {
                var current = report.HwProbeVersion == CapabilityReport.CurrentHwProbeVersion && IsCurrentFfmpeg(report.Ffmpeg);
                entries.Add(new SpeedHistoryEntry(Path.GetFileNameWithoutExtension(file), report.GeneratedUtc, report.Method.ToString(), report.Results.Select(r => r.Test).Distinct(StringComparer.Ordinal).Count(), current));
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

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

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

        var comparisons = SpeedComparison.None;
        foreach (var name in request.Comparisons)
        {
            if (!Enum.TryParse<SpeedComparison>(name, ignoreCase: true, out var comparison) || comparison == SpeedComparison.None || !Enum.IsDefined(comparison))
            {
                return null;
            }

            comparisons |= comparison;
        }

        if (!Catalog.Default.Repeats.Any(o => o.Value == request.Repeats) || !Catalog.Default.TimeLimits.Any(o => o.Value == request.TimeLimitSeconds))
        {
            return null;
        }

        return new SpeedOptions(method, videos, outputs, comparisons, new SpeedSettings())
        {
            File = file,
            Repeats = request.Repeats,
            TimeLimit = request.TimeLimitSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
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
    };

    /// <summary>Matches a history ID: the UTC time a run finished.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^\d{8}T\d{6}Z$")]
    private static partial Regex HistoryId();

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
        var options = ServerEngineOptions(mediaEncoder, paths);
        var environment = EnvironmentRules.InServer(baseline.Values, new Dictionary<string, string>());
        var recorder = new RecordingFfmpegRunner(new FfmpegRunner());
        CapabilityReport report;
        using (var engine = new ProbeEngine(recorder, arguments, new HostPlatform(), TimeProvider.System, environment))
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

    /// <summary>Runs a speed measurement; the caller already holds the gate, which this releases.</summary>
    /// <param name="measure">The speed run.</param>
    /// <param name="speed">What to measure.</param>
    /// <param name="backends">The working backends.</param>
    /// <returns>The result.</returns>
    private async Task<ProbeRunResult> RunSpeedHeldAsync(
        Func<SpeedOptions, IReadOnlyCollection<(HwType Type, string Device)>, IProgress<SpeedProgress>, CancellationToken, Task<SpeedReport>> measure,
        SpeedOptions speed,
        IReadOnlyCollection<(HwType Type, string Device)> backends)
    {
        _status = _status with { State = ProbeState.Running, Activity = ProbeActivity.Speed, LastStartedUtc = _time.GetUtcNow(), LastError = null, Done = 0, Total = null };
        try
        {
            lock (_speedSoFar)
            {
                _speedSoFar.Clear();
                _speedRunning = speed;
            }

            var progress = new DirectProgress(p =>
            {
                if (p.Result is { } result)
                {
                    lock (_speedSoFar)
                    {
                        _speedSoFar.Add(result);
                    }
                }

                _status = _status with { Done = p.Done, Total = p.Total };
            });
            var report = await measure(speed, backends, progress, CancellationToken.None);
            await SpeedReportStore.WriteAsync(report, SpeedPath, CancellationToken.None);
            await SaveSpeedHistoryAsync(report);
            Log.SpeedCompleted(_logger, report.Results.Count);
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
            }

            _status = _status with { State = ProbeState.Idle, LastCompletedUtc = _time.GetUtcNow(), Done = null, Total = null };
            _gate.Release();
        }
    }

    /// <summary>Reports progress on the caller's thread, in order.</summary>
    /// <param name="report">Applies one report.</param>
    /// <remarks><see cref="Progress{T}"/> posts to the thread pool, so a late report could mark a finished run as running again.</remarks>
    private sealed class DirectProgress(Action<SpeedProgress> report) : IProgress<SpeedProgress>
    {
        /// <inheritdoc/>
        public void Report(SpeedProgress value) => report(value);
    }
}
