using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Storage;
using Jellyfin.Plugin.HwProbe.Core.Verdict;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Measures how fast each working backend, and software, runs the chosen transcodes.</summary>
public sealed class SpeedEngine : IDisposable
{
    // Samples are downloads; keyed apart from the ffmpeg build so an ffmpeg update doesn't fetch them again.
    private const string SamplesCacheKey = "samples";

    // A single copy is expected to take about Content; this leaves room for slow hosts, whose fps comes from the frames reached.
    private static readonly TimeSpan _singleTimeout = TimeSpan.FromSeconds(30);

    // Copies that need much longer than real time have already fallen behind.
    private static readonly TimeSpan _copiesGrace = TimeSpan.FromSeconds(10);

    private readonly IFfmpegRunner _runner;
    private readonly IArgumentSourceFactory _arguments;
    private readonly IHostPlatform _platform;
    private readonly TimeProvider _time;
    private readonly EnvironmentRules _environment;
    private readonly SerialProbeGate _gate;

    /// <summary>When the last measurement ended, for the delay before the next; runs are serial, so one value serves them all.</summary>
    private DateTimeOffset? _lastMeasured;

    /// <summary>Initializes a new instance of the <see cref="SpeedEngine"/> class.</summary>
    /// <param name="runner">Launches ffmpeg.</param>
    /// <param name="arguments">Generates arguments per device.</param>
    /// <param name="platform">Host access.</param>
    /// <param name="time">Clock for the report timestamp.</param>
    /// <param name="environment">How to treat the process environment EncodingHelper writes to.</param>
    public SpeedEngine(IFfmpegRunner runner, IArgumentSourceFactory arguments, IHostPlatform platform, TimeProvider time, EnvironmentRules environment)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(environment);

        _runner = runner;
        _arguments = arguments;
        _platform = platform;
        _time = time;
        _environment = environment;

        // As in ProbeEngine: inside the server, restoring would undo variables its own transcodes rely on.
        _gate = new SerialProbeGate(environment.RestoreAfterGeneration ? EncodingHelperEnvironment.Variables : []);
    }

    /// <summary>Gets the downloader for clips that can't be generated, such as the PGS sample.</summary>
    public IFixtureDownloader FixtureDownloader { get; init; } = new HttpFixtureDownloader();

    /// <summary>Gets what receives each measurement's command line, ffmpeg's path first, as it starts, or null.</summary>
    public Action<string>? CommandLog { get; init; }

    /// <summary>Returns where measurements are kept for reuse: beside the clip cache.</summary>
    /// <param name="options">The cache locations.</param>
    /// <returns>The cache.</returns>
    public static SpeedResultCache ResultCacheFor(EngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SpeedResultCache(Path.Join(Path.GetDirectoryName(Path.GetFullPath(options.FixturesDirectory)), "speed-results"));
    }

    /// <summary>Measures every backend, then software, on every chosen test.</summary>
    /// <param name="options">The ffmpeg and cache locations; the stage and filters are ignored.</param>
    /// <param name="speed">What to measure.</param>
    /// <param name="backends">The backends and devices the probe found working.</param>
    /// <param name="progress">Receives each result as it's measured, with the count done and the total, or null.</param>
    /// <param name="cancellationToken">Cancels the run; running ffmpeg trees are killed.</param>
    /// <returns>The report.</returns>
    /// <exception cref="FfmpegUnusableException">ffmpeg is too old, Libav, or produced no version.</exception>
    public async Task<SpeedReport> RunAsync(EngineOptions options, SpeedOptions speed, IReadOnlyCollection<(HwType Type, string Device)> backends, IProgress<SpeedProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(speed);
        ArgumentNullException.ThrowIfNull(backends);

        var tests = speed.Resolve();
        bool Chosen(HwType type) => speed.Backends?.Contains(type) != false;
        List<(HwType Type, string Device)> measured = [.. backends.Where(b => b.Type != HwType.none && Chosen(b.Type)), .. Chosen(HwType.none) ? [(HwType.none, string.Empty)] : Array.Empty<(HwType, string)>()];

        // Audio is decoded and encoded on the CPU whatever the backend, so it's measured in software alone.
        static bool Runs(HwType type, SpeedTest test) => type == HwType.none || !test.AudioOnly;

        // Planned before any clip exists, so the page can show the whole table from the start.
        var total = measured.Sum(b => tests.Count(t => Runs(b.Type, t)));
        progress?.Report(new SpeedProgress(0, total, null)
        {
            Planned = [.. measured.SelectMany(b => tests.Where(t => Runs(b.Type, t)).Select(t => Describe(t, new SpeedResult(b.Type, b.Device, t.Key, string.Empty, null, null, false, null) { Pending = true })))],
        });

        var host = new HostInfoReader(_platform).Read();
        var ffmpeg = options.Ffmpeg.Path;
        var caps = await new FfmpegCapabilityProbe(_runner, options.ProbeTimeout).ProbeAsync(ffmpeg, cancellationToken);
        if (caps.Validation != FfmpegValidation.Valid)
        {
            throw new FfmpegUnusableException($"{ffmpeg}: {caps.Validation}.");
        }

        var done = 0;
        var cancelled = false;
        List<SpeedResult> results = [];
        try
        {
            Dictionary<string, string> names = new(StringComparer.Ordinal);
            foreach (var test in tests)
            {
                if (test.Fixture is { } fixture)
                {
                    names.TryAdd(fixture.FileName, test.Name ?? fixture.FileName);
                }
            }

            var resultsCache = ResultCacheFor(options);
            resultsCache.Prune(caps.VersionLine);
            if (speed.Pause is { } waitFirst)
            {
                await waitFirst.HoldWhileBusyAsync(cancellationToken);
            }

            // Only clips that aren't cached count. Each new clip's first step means the one before it is done.
            var clipsTotal = 0;
            HashSet<string> begun = new(StringComparer.Ordinal);
            var clipSteps = progress is null ? null : new FixtureStepProgress(step =>
            {
                begun.Add(step.Spec.FileName);
                progress.Report(new SpeedProgress(0, total, null) { Preparing = Preparing(step, names), ClipsDone = Math.Min(begun.Count - 1, clipsTotal), ClipsTotal = clipsTotal });
            });
            var clips = await BuildClipsAsync(options, caps, tests, speed.Settings, clipSteps, pending => clipsTotal = pending, cancellationToken);
            progress?.Report(new SpeedProgress(0, total, null) { ClipsDone = clipsTotal, ClipsTotal = clipsTotal });
            foreach (var (type, device) in measured)
            {
                if (speed.Pause is { } beforeOpen)
                {
                    await beforeOpen.HoldWhileBusyAsync(cancellationToken);
                }

                var traits = await OpenAsync(options, type, device, host.Os, cancellationToken);
                var source = traits is null ? null : _arguments.Create(caps, traits);
                foreach (var test in tests.Where(t => Runs(type, t)))
                {
                    if (speed.Pause is { } pause)
                    {
                        await pause.WaitAsync(cancellationToken);
                    }

                    var missing = MissingClip(test, speed.Settings, clips);
                    if (test.AudioOnly)
                    {
                        var audio = missing is null ? SpeedVariants.Audio(test, speed.Settings, Paths(clips)) : null;
                        var measuredAudio = source is null ? new SpeedResult(type, device, test.Key, string.Empty, null, null, false, "The device didn't open.")
                            : audio is null ? new SpeedResult(type, device, test.Key, string.Empty, null, null, false, missing)
                            : await MeasureOrReuseAsync(options, speed, caps.VersionLine, resultsCache, type, device, test, ct => AudioCommandAsync(source, audio, ct), (timeUp, ct) => MeasureAudioAsync(options, speed.Method, speed.MeasureResources, source, test, audio, timeUp, ct), cancellationToken);
                        var describedAudio = Describe(test, measuredAudio);
                        results.Add(describedAudio);
                        progress?.Report(new SpeedProgress(++done, total, describedAudio) { ClipsDone = clipsTotal, ClipsTotal = clipsTotal });
                        continue;
                    }

                    var cell = SpeedVariants.ForBackend(type, test, SpeedVariants.Base(test, speed.Settings, Paths(clips)), speed.Settings);

                    // A codec the probe found this GPU can't decode is left unticked, as the probe advises, so Jellyfin decodes it in software.
                    // The probe's key names the codec, depth, and profile, and the decoder the settings pick (QSV's own or the native one, CUVID or NVDEC).
                    var decoder = type == HwType.qsv && !speed.Settings.PreferNativeDecoder ? "_qsvdecoder" : type == HwType.nvenc && !speed.Settings.EnhancedNvdec ? "_cuvid" : string.Empty;
                    var probeSoftware = speed.DecodeUnsupported.Contains((type, device, Probes.MatrixCatalog.Key(cell.InputCodec, cell.BitDepth, cell.Profile) + decoder));
                    if (probeSoftware)
                    {
                        cell = cell with { HardwareDecode = false };
                    }

                    // Jellyfin would ask for low power anyway and ffmpeg would drop it, so the run would only repeat normal mode.
                    var noLowPower = cell.LowPower && test.OutputCodec is { } codec && speed.LowPowerUnsupported.Contains((type, device, codec))
                        ? $"Not measured: this GPU has no low-power {SpeedTestText.CodecName(codec)} encoder, so ffmpeg would encode in normal mode."
                        : null;
                    var result = source is null ? new SpeedResult(type, device, test.Key, string.Empty, null, null, false, "The device didn't open.")
                        : missing is not null ? new SpeedResult(type, device, test.Key, string.Empty, null, null, false, missing)
                        : noLowPower is not null ? new SpeedResult(type, device, test.Key, string.Empty, null, null, false, noLowPower)
                        : await MeasureOrReuseAsync(options, speed, caps.VersionLine, resultsCache, type, device, test, ct => CommandAsync(source, type, device, test, cell, ct), (timeUp, ct) => MeasureAsync(options, speed.Method, speed.MeasureResources, source, type, device, test, cell, timeUp, ct), cancellationToken);
                    var described = Describe(test, result);
                    results.Add(described);
                    progress?.Report(new SpeedProgress(++done, total, described) { ClipsDone = clipsTotal, ClipsTotal = clipsTotal });
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        return new SpeedReport(
            _time.GetUtcNow(),
            new FfmpegSummary(ffmpeg, options.Ffmpeg.Source.ToString(), caps.Version?.ToString() ?? "unknown", caps.IsJellyfinBuild),
            speed.Method,
            results)
        {
            Settings = speed.Settings,
            Repeats = speed.Repeats,
            Cancelled = cancelled,
        };
    }

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

    /// <summary>Measures, deferring to the server's own transcodes: it waits while one runs, and a measurement one interrupts is stopped and started again once it ends.</summary>
    /// <param name="pause">The run's pause, with the busy check; null or without one, this just measures.</param>
    /// <param name="measure">Measures once.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The result of a measurement nothing interrupted.</returns>
    private static async Task<SpeedResult> MeasureDeferringAsync(SpeedPause? pause, Func<CancellationToken, Task<SpeedResult>> measure, CancellationToken cancellationToken)
    {
        if (pause?.Busy is not { } busy)
        {
            return await measure(cancellationToken);
        }

        while (true)
        {
            await pause.HoldWhileBusyAsync(cancellationToken);

            // A pause asked for while deferring takes effect before measuring again.
            await pause.WaitAsync(cancellationToken);
            using var interrupt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var watch = Task.Run(
                async () =>
                {
                    while (!interrupt.IsCancellationRequested)
                    {
                        await Task.Delay(pause.BusyCheck, interrupt.Token);
                        if (busy())
                        {
                            // Kills the running ffmpeg trees, so the server's transcode isn't competing with them.
                            await interrupt.CancelAsync();
                        }
                    }
                },
                CancellationToken.None);
            try
            {
                return await measure(interrupt.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Interrupted by a transcode: wait for it to end, then measure this one again.
            }
            finally
            {
                await interrupt.CancelAsync();
                try
                {
                    await watch;
                }
                catch (OperationCanceledException)
                {
                    // Stopped with the measurement; a failed busy check still surfaces.
                }
            }
        }
    }

    /// <summary>Returns why a test's clips aren't available, or null when they are.</summary>
    /// <param name="test">The test.</param>
    /// <param name="settings">The settings, for the subtitles burned in.</param>
    /// <param name="clips">Every clip built.</param>
    /// <returns>The reason, or null.</returns>
    private static string? MissingClip(SpeedTest test, SpeedSettings settings, Dictionary<string, FixtureResult> clips) =>
        SpeedVariants.Clips([test], settings).Select(f => clips[f.FileName]).FirstOrDefault(c => c.Status != FixtureStatus.Available) is { } missing
            ? $"No {missing.Spec.FileName} clip: {missing.Reason}"
            : null;

    /// <summary>Returns available clip paths by file name.</summary>
    /// <param name="clips">Every clip built.</param>
    /// <returns>The paths.</returns>
    private static Dictionary<string, string> Paths(Dictionary<string, FixtureResult> clips)
    {
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        foreach (var clip in clips.Values)
        {
            if (clip.Path is { } path)
            {
                paths[clip.Spec.FileName] = path;
            }
        }

        return paths;
    }

    /// <summary>Adds what the page and report show about a test to its result.</summary>
    /// <param name="test">The test.</param>
    /// <param name="result">The result.</param>
    /// <returns>The described result.</returns>
    private static SpeedResult Describe(SpeedTest test, SpeedResult result) =>
        result with { Label = test.Label, Video = test.Name, Output = test.OutputLabel, Input = SpeedTestText.Input(test), FrameRate = result.FrameRate ?? test.FrameRate, Credit = test.Credit, LicenseUrl = test.LicenseUrl, Kind = test.Kind };

    /// <summary>Returns the SHA-256 of an ffmpeg command line, so runs can tell whether a setting changed it.</summary>
    /// <param name="command">The command line.</param>
    /// <returns>The hash in lowercase hex.</returns>
    private static string CommandHash(string command) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(command)));

    /// <summary>Generates a test's arguments: image extraction's for an image cell, a transcode's or decode's otherwise.</summary>
    /// <param name="source">The device's argument source.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device.</param>
    /// <param name="cell">The cell to generate.</param>
    /// <returns>The arguments.</returns>
    private static ProbeArguments Arguments(IArgumentSource source, HwType type, string device, ProbeCell cell) =>
        cell.Images is { } job ? source.BuildImages(type, device.Length == 0 ? null : device, cell, job) : source.Build(type, device.Length == 0 ? null : device, cell);

    /// <summary>Builds a test's command line.</summary>
    /// <param name="args">The test's arguments.</param>
    /// <param name="test">The test.</param>
    /// <param name="content">How much of the looped source to process.</param>
    /// <returns>The ffmpeg argument string.</returns>
    private static string CommandLine(ProbeArguments args, SpeedTest test, TimeSpan content) =>
        test.Images ? SpeedCommandLine.BuildImages(args, content, test.StartAt) : SpeedCommandLine.Build(args, content, test.DecodeOnly, test.StartAt);

    /// <summary>Returns how a test is paced.</summary>
    /// <param name="test">The test.</param>
    /// <returns>The pace.</returns>
    private static MeterPace Pace(SpeedTest test) => test.Images ? MeterPace.Images : MeterPace.Frames;

    /// <summary>Measures a variant as many times as asked and reports the median.</summary>
    /// <param name="speed">What to measure, for the repeats and time limit.</param>
    /// <param name="measure">Measures once, given when the time limit has passed.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The median result, by fps.</returns>
    private static async Task<SpeedResult> MeasureRepeatedAsync(SpeedOptions speed, Func<Func<bool>, CancellationToken, Task<SpeedResult>> measure, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool TimeUp() => speed.TimeLimit is { } limit && clock.Elapsed >= limit;
        List<SpeedResult> runs = [];
        for (var i = 0; i < Math.Max(1, speed.Repeats); i++)
        {
            var run = await measure(TimeUp, cancellationToken);
            runs.Add(run);

            // Nothing to repeat when it couldn't be measured.
            if (run.Fps is null)
            {
                return run;
            }

            if (TimeUp())
            {
                break;
            }
        }

        var sorted = runs.OrderBy(r => r.Fps).ToList();
        var median = sorted[sorted.Count / 2];
        var streams = runs.Select(r => r.Streams ?? 0).Order().ToList()[runs.Count / 2];

        // Fewer repeats than asked, because the time limit came first, count as cut off too.
        var interrupted = runs.Count < Math.Max(1, speed.Repeats) || runs.Any(r => r.Interrupted);
        return median with { Streams = median.Streams is null ? null : streams, Interrupted = interrupted };
    }

    /// <summary>Describes a clip being made or downloaded.</summary>
    /// <param name="step">The step.</param>
    /// <param name="names">Video names by clip file name.</param>
    /// <returns>e.g. <c>Downloading Animation: 5 of 14 MB</c>.</returns>
    private static string Preparing(FixtureStep step, Dictionary<string, string> names)
    {
        var name = names.GetValueOrDefault(step.Spec.FileName)
            ?? (step.Spec.FileName == SpeedCatalog.TextSubtitles.FileName ? "text subtitles" : step.Spec.FileName == SpeedCatalog.ImageSubtitles.FileName ? "PGS subtitles" : step.Spec.FileName);
        return step.Describe(name);
    }

    /// <summary>Makes or downloads the clips the tests need.</summary>
    /// <param name="options">Cache locations and timeouts.</param>
    /// <param name="caps">Build capabilities, for the software encoders.</param>
    /// <param name="tests">The tests.</param>
    /// <param name="settings">The settings, for the subtitles burned in.</param>
    /// <param name="progress">Receives each clip being made or downloaded, or null.</param>
    /// <param name="planned">Receives how many clips aren't cached, before any is made.</param>
    /// <param name="cancellationToken">Cancels generation.</param>
    /// <returns>Each clip by file name.</returns>
    private async Task<Dictionary<string, FixtureResult>> BuildClipsAsync(EngineOptions options, FfmpegCapabilities caps, IReadOnlyList<SpeedTest> tests, SpeedSettings settings, IProgress<FixtureStep>? progress, Action<int> planned, CancellationToken cancellationToken)
    {
        var clips = SpeedVariants.Clips(tests, settings);
        var key = Fingerprint.Compute(new FingerprintInputs(options.Ffmpeg.Path, caps.VersionLine, null, null, null, null, null, null));
        FixtureCacheContents.Prune(options.FixturesDirectory, key);
        var builders = clips.GroupBy(c => c.KeepAcrossBuilds)
            .Select(g => (Key: g.Key ? SamplesCacheKey : key, Builder: new FixtureBuilder(_runner, options.Ffmpeg.Path, options.FixturesDirectory, options.FixtureTimeout, FixtureDownloader, [.. g], null) { Progress = progress }))
            .ToList();
        var pending = 0;
        foreach (var (cacheKey, builder) in builders)
        {
            pending += await builder.PendingAsync(cacheKey, caps.Encoders, cancellationToken);
        }

        planned(pending);
        Dictionary<string, FixtureResult> built = new(StringComparer.Ordinal);
        foreach (var (cacheKey, builder) in builders)
        {
            foreach (var clip in await builder.BuildAsync(cacheKey, caps.Encoders, cancellationToken))
            {
                built[clip.Spec.FileName] = clip;
            }
        }

        return built;
    }

    /// <summary>Opens a backend's device for its traits, as the probe does.</summary>
    /// <param name="options">The ffmpeg and timeout.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device.</param>
    /// <param name="os">The host OS.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The traits, or null when the device didn't open.</returns>
    private async Task<DeviceTraits?> OpenAsync(EngineOptions options, HwType type, string device, HostOs os, CancellationToken cancellationToken)
    {
        if (type == HwType.none || DeviceOpenProbe.Arguments(type, device, os) is not { } arguments)
        {
            return new DeviceTraits(VaapiDriver.Other);
        }

        var invocation = new FfmpegInvocation(options.Ffmpeg.Path, arguments, _environment.Baseline, options.ProbeTimeout);
        var open = DeviceOpenProbe.Evaluate(type, await _gate.RunAsync(ct => _runner.RunAsync(invocation, ct), cancellationToken));
        return open.Outcome == ProbeOutcome.Pass ? new DeviceTraits(open.Driver) : null;
    }

    /// <summary>Reuses a measurement an earlier run saved with exactly the same inputs when asked, or measures and saves it.</summary>
    /// <param name="options">The ffmpeg.</param>
    /// <param name="speed">The run.</param>
    /// <param name="ffmpegVersion">The ffmpeg version line.</param>
    /// <param name="cache">The saved measurements.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device.</param>
    /// <param name="test">The test.</param>
    /// <param name="commandFor">Generates the command and environment for the reuse key, or null when none can be built.</param>
    /// <param name="measure">Measures once, given when the time limit has passed.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The result.</returns>
    private async Task<SpeedResult> MeasureOrReuseAsync(
        EngineOptions options,
        SpeedOptions speed,
        string ffmpegVersion,
        SpeedResultCache cache,
        HwType type,
        string device,
        SpeedTest test,
        Func<CancellationToken, Task<string?>> commandFor,
        Func<Func<bool>, CancellationToken, Task<SpeedResult>> measure,
        CancellationToken cancellationToken)
    {
        // Keyed on the command Jellyfin's EncodingHelper generates, so any setting, server or plugin change that alters it misses.
        var command = await commandFor(cancellationToken);
        var key = command is null ? null : SpeedResultCache.Key(options.Ffmpeg.Path, ffmpegVersion, type, device, test, command, speed);
        if (key is not null && speed.ReuseResults && await cache.GetAsync(key, cancellationToken) is { } earlier)
        {
            return earlier.Result with { ReusedFromUtc = earlier.MeasuredUtc };
        }

        // Audio keeps one CPU core busy for a second or two, so it doesn't wait for the system to wind down.
        if (!test.AudioOnly && _lastMeasured is { } last && speed.TestDelay - (_time.GetUtcNow() - last) is { Ticks: > 0 } wait)
        {
            await Task.Delay(wait, _time, cancellationToken);
        }

        SpeedResult result;
        try
        {
            result = await MeasureDeferringAsync(speed.Pause, ct => MeasureRepeatedAsync(speed, measure, ct), cancellationToken);
        }
        finally
        {
            _lastMeasured = _time.GetUtcNow();
        }

        // Only a full measurement is worth reusing: a failure may be fixed by the next run, and a run cut off by a timeout or the time limit is short of what a full one measures.
        if (key is not null && result.Fps is not null && !result.Interrupted)
        {
            await cache.SaveAsync(key, new SpeedCacheEntry(_time.GetUtcNow(), SpeedResultCache.MeasurementVersion, ffmpegVersion, result), cancellationToken);
        }

        return result;
    }

    /// <summary>Generates a test's command and environment, inside the probe lock, for the reuse key.</summary>
    /// <param name="source">The device's argument source.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device.</param>
    /// <param name="test">The test.</param>
    /// <param name="cell">The cell to generate.</param>
    /// <param name="cancellationToken">Cancels waiting for the lock.</param>
    /// <returns>The command line and environment, or null when no command can be built.</returns>
    private Task<string?> CommandAsync(IArgumentSource source, HwType type, string device, SpeedTest test, ProbeCell cell, CancellationToken cancellationToken) =>
        _gate.RunAsync(
            ct =>
            {
                try
                {
                    var args = Arguments(source, type, device, cell);
                    var environment = string.Join('\n', args.Environment.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + "=" + e.Value));
                    return Task.FromResult<string?>(CommandLine(args, test, Pace(test).Content) + "\n" + environment);
                }
                catch (Exception e) when (e is ArgumentConstructionException or UnsafeProbeException or NotSupportedException)
                {
                    return Task.FromResult<string?>(null);
                }
            },
            cancellationToken);

    /// <summary>Generates an audio test's command and environment, inside the probe lock, for the reuse key.</summary>
    /// <param name="source">The argument source.</param>
    /// <param name="audio">The cell to generate.</param>
    /// <param name="cancellationToken">Cancels waiting for the lock.</param>
    /// <returns>The command line and environment.</returns>
    private Task<string?> AudioCommandAsync(IArgumentSource source, AudioCell audio, CancellationToken cancellationToken) =>
        _gate.RunAsync(
            ct =>
            {
                var args = source.BuildAudio(audio);
                var environment = string.Join('\n', args.Environment.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + "=" + e.Value));
                return Task.FromResult<string?>(SpeedCommandLine.BuildAudio(args, MeterPace.Audio.Content) + "\n" + environment);
            },
            cancellationToken);

    /// <summary>Generates an audio test's arguments and measures them in software, inside the probe lock.</summary>
    /// <param name="options">The ffmpeg.</param>
    /// <param name="method">How streams are counted.</param>
    /// <param name="measureResources">Whether the single copy is measured for CPU and memory usage.</param>
    /// <param name="source">The argument source.</param>
    /// <param name="test">The test.</param>
    /// <param name="audio">The cell to generate.</param>
    /// <param name="timeUp">Reports when the measurement's time limit has passed.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The result, with fps as the seconds of audio done each second.</returns>
    private Task<SpeedResult> MeasureAudioAsync(EngineOptions options, SpeedMethod method, bool measureResources, IArgumentSource source, SpeedTest test, AudioCell audio, Func<bool> timeUp, CancellationToken cancellationToken) =>
        _gate.RunAsync(
            async ct =>
            {
                var args = source.BuildAudio(audio);
                var pace = MeterPace.Audio;
                string Command(TimeSpan content) => SpeedCommandLine.BuildAudio(args, content);
                CommandLog?.Invoke($"{options.Ffmpeg.Path} {Command(pace.Content)}");
                var measured = await SpeedMeter.MeasureAsync(Launcher(options, Command, args.Environment, measureResources, pace, null), method, test.FrameRate, !test.DecodeOnly, ct, timeUp, pace);
                return new SpeedResult(HwType.none, string.Empty, test.Key, string.Empty, measured.Fps, measured.Streams, measured.Capped, measured.Note) { Interrupted = measured.Interrupted, Command = CommandHash(Command(pace.Content)), Resources = measured.Resources, FrameRate = test.FrameRate };
            },
            cancellationToken);

    /// <summary>Returns what launches a measurement's copies together, each processing as much content as asked.</summary>
    /// <param name="options">The ffmpeg.</param>
    /// <param name="command">Builds the command line for an amount of content.</param>
    /// <param name="environment">The environment the copies run with.</param>
    /// <param name="measureResources">Whether a single copy is measured for resource usage.</param>
    /// <param name="pace">The pace, for the copies' timeout.</param>
    /// <param name="inspect">Sees each launch's results, or null.</param>
    /// <returns>The launcher.</returns>
    private Func<int, TimeSpan, CancellationToken, Task<IReadOnlyList<FfmpegRunResult>>> Launcher(EngineOptions options, Func<TimeSpan, string> command, IReadOnlyDictionary<string, string?> environment, bool measureResources, MeterPace pace, Action<IReadOnlyList<FfmpegRunResult>>? inspect) =>
        async (copies, content, token) =>
        {
            var invocation = new FfmpegInvocation(options.Ffmpeg.Path, command(content), environment, copies == 1 ? _singleTimeout : pace.Content + _copiesGrace) { MeasureResources = measureResources && copies == 1 };
            var runs = await Task.WhenAll(Enumerable.Range(0, copies).Select(_ => _runner.RunAsync(invocation, token)));
            inspect?.Invoke(runs);
            return runs;
        };

    /// <summary>Generates one variant's arguments and measures them, inside the probe lock.</summary>
    /// <param name="options">The ffmpeg.</param>
    /// <param name="method">How streams are counted.</param>
    /// <param name="measureResources">Whether the single copy is measured for CPU, memory, and GPU usage.</param>
    /// <param name="source">The device's argument source.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device.</param>
    /// <param name="test">The test.</param>
    /// <param name="cell">The cell to generate.</param>
    /// <param name="timeUp">Reports when the measurement's time limit has passed.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The result.</returns>
    private Task<SpeedResult> MeasureAsync(
        EngineOptions options,
        SpeedMethod method,
        bool measureResources,
        IArgumentSource source,
        HwType type,
        string device,
        SpeedTest test,
        ProbeCell cell,
        Func<bool> timeUp,
        CancellationToken cancellationToken) =>
        _gate.RunAsync(
            async ct =>
            {
                ProbeArguments args;
                try
                {
                    args = Arguments(source, type, device, cell);
                }
                catch (ArgumentConstructionException)
                {
                    // No hardware arguments at all, or for images a backend whose decoders don't skip to key frames: Jellyfin would do the whole job in software.
                    return new SpeedResult(type, device, test.Key, string.Empty, null, null, false, test.Images ? Data.Catalog.Text("noteImagesKeyFrames") : test.DecodeOnly ? "Not measured: decoded in software with this backend." : "Not measured: decoded and encoded in software with this backend.");
                }
                catch (UnsafeProbeException ex)
                {
                    return new SpeedResult(type, device, test.Key, string.Empty, null, null, false, ex.Message);
                }
                catch (NotSupportedException)
                {
                    // The command-line tool has no subtitle encoder to extract a file's internal text subtitles with.
                    return new SpeedResult(type, device, test.Key, string.Empty, null, null, false, "Burning in a file's own text subtitles requires Jellyfin; measure it from the plugin.");
                }

                // A hardware column needs the step it's about on the GPU: the encode for a transcode, the decode for a decode test or images. Software has its own column.
                var softwareDecode = type != HwType.none && args.Hwaccel is null && args.HardwareDecoder is null;
                var softwareStep = type == HwType.none ? null
                    : test.DecodeOnly || test.Images ? (softwareDecode ? "decoded" : null)
                    : args.HardwareEncoder ? null
                    : "encoded";

                // Decoding the probe found failing is named as the reason, as the advice to untick that codec is.
                if (softwareStep is not null)
                {
                    return new SpeedResult(type, device, test.Key, string.Empty, null, null, false, softwareStep == "decoded" && !cell.HardwareDecode ? Data.Catalog.Text("noteDecodeFailed") : test.Images ? Data.Catalog.Text("noteImagesSoftware") : $"Not measured: {softwareStep} in software with this backend.");
                }

                var note = !softwareDecode ? null : Data.Catalog.Text(cell.HardwareDecode ? "noteSoftwareDecode" : "noteSoftwareDecodeFailed");
                var width = Math.Min(args.OutputWidth ?? test.Width, test.Width);
                var size = test.DecodeOnly || test.Images ? null : SpeedTestText.Resolution(width, test.Height * width / test.Width, false);
                var pace = Pace(test);

                string Command(TimeSpan content) => CommandLine(args, test, content);

                // jellyfin-ffmpeg's qsvenc drops low-power mode it can't use and carries on (debian/patches/0071), so the run measures normal mode.
                var lowPowerDropped = false;
                var launch = Launcher(options, Command, args.Environment, measureResources, pace, runs => lowPowerDropped |= cell.LowPower && runs.Any(r => StderrMarkers.LowPowerDisabled.Any(m => r.Stderr.Contains(m, StringComparison.Ordinal))));

                // Double-rate deinterlacing makes a frame per field, so real time is twice the source rate (EncodingHelper.GetSwDeinterlaceFilter and the hardware deinterlace filters, v12.2: interlaced sources of 30 fps or less).
                // Images are extracted ahead of playback, so their speed has no real time to keep up with, and no streams are counted.
                var outputRate = cell.DoubleRate && test.Interlaced && !test.DecodeOnly && !test.Images && test.FrameRate <= 30 ? test.FrameRate * 2 : test.FrameRate;
                CommandLog?.Invoke($"{options.Ffmpeg.Path} {Command(pace.Content)}");
                var measured = await SpeedMeter.MeasureAsync(launch, method, outputRate, !test.DecodeOnly && !test.Images, ct, timeUp, pace);
                note = lowPowerDropped ? Data.Catalog.Text("noteLowPowerDropped") : note;
                return new SpeedResult(type, device, test.Key, string.Empty, measured.Fps, measured.Streams, measured.Capped, measured.Note ?? note) { OutputSize = size, Interrupted = measured.Interrupted, Command = CommandHash(Command(pace.Content)), Resources = measured.Resources, LowPowerDropped = lowPowerDropped, FrameRate = outputRate };
            },
            cancellationToken);
}
