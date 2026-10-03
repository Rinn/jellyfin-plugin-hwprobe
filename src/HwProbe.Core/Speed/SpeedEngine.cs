using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Storage;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Measures how fast each working backend, and software, runs the chosen transcodes.</summary>
public sealed class SpeedEngine : IDisposable
{
    // Samples are downloads; keyed apart from the ffmpeg build so an ffmpeg update doesn't fetch them again.
    private const string SamplesCacheKey = "samples";

    // A single copy is expected to take about Content; this leaves room for slow hosts, whose fps comes from the frames reached.
    private static readonly TimeSpan _singleTimeout = TimeSpan.FromSeconds(30);

    // Copies that need much longer than real time have already fallen behind.
    private static readonly TimeSpan _copiesTimeout = SpeedMeter.Content + TimeSpan.FromSeconds(10);

    private readonly IFfmpegRunner _runner;
    private readonly IArgumentSourceFactory _arguments;
    private readonly IHostPlatform _platform;
    private readonly TimeProvider _time;
    private readonly EnvironmentRules _environment;
    private readonly SerialProbeGate _gate;

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

        // Planned before any clip exists, so the page can show the whole table from the start.
        var total = measured.Count * tests.Count;
        progress?.Report(new SpeedProgress(0, total, null)
        {
            Planned = [.. measured.SelectMany(b => tests.Select(t => Describe(t, new SpeedResult(b.Type, b.Device, t.Key, string.Empty, null, null, false, null) { Pending = true })))],
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
            var names = tests.Where(t => t.Fixture is not null).GroupBy(t => t.Fixture!.FileName).ToDictionary(g => g.Key, g => g.First().Name ?? g.Key, StringComparer.Ordinal);
            var clips = await BuildClipsAsync(options, caps, tests, speed.Settings, progress is null ? null : new StepProgress(step => progress.Report(new SpeedProgress(0, total, null) { Preparing = Preparing(step, names) })), cancellationToken);
            progress?.Report(new SpeedProgress(0, total, null));
            foreach (var (type, device) in measured)
            {
                var traits = await OpenAsync(options, type, device, host.Os, cancellationToken);
                var source = traits is null ? null : _arguments.Create(caps, traits);
                foreach (var test in tests)
                {
                    if (speed.Pause is { } pause)
                    {
                        await pause.WaitAsync(cancellationToken);
                    }

                    var missing = MissingClip(test, speed.Settings, clips);
                    var result = source is null ? new SpeedResult(type, device, test.Key, string.Empty, null, null, false, "The device didn't open.")
                        : missing is not null ? new SpeedResult(type, device, test.Key, string.Empty, null, null, false, missing)
                        : await MeasureRepeatedAsync(options, speed, source, type, device, test, SpeedVariants.ForBackend(type, test, SpeedVariants.Base(test, speed.Settings, Paths(clips)), speed.Settings), cancellationToken);
                    var described = Describe(test, result);
                    results.Add(described);
                    progress?.Report(new SpeedProgress(++done, total, described));
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
    private static Dictionary<string, string> Paths(Dictionary<string, FixtureResult> clips) =>
        clips.Values.Where(c => c.Path is not null).ToDictionary(c => c.Spec.FileName, c => c.Path!, StringComparer.Ordinal);

    /// <summary>Adds what the page and report show about a test to its result.</summary>
    /// <param name="test">The test.</param>
    /// <param name="result">The result.</param>
    /// <returns>The described result.</returns>
    private static SpeedResult Describe(SpeedTest test, SpeedResult result) =>
        result with { Label = test.Label, Video = test.Name, Output = test.OutputLabel, Input = SpeedTestText.Input(test), FrameRate = test.FrameRate, Credit = test.Credit, LicenseUrl = test.LicenseUrl };

    /// <summary>Describes a clip being made or downloaded.</summary>
    /// <param name="step">The step.</param>
    /// <param name="names">Video names by clip file name.</param>
    /// <returns>e.g. <c>Downloading Animation: 5 of 14 MB</c>.</returns>
    private static string Preparing(FixtureStep step, Dictionary<string, string> names)
    {
        var name = names.GetValueOrDefault(step.Spec.FileName)
            ?? (step.Spec.FileName == SpeedCatalog.TextSubtitles.FileName ? "text subtitles" : step.Spec.FileName == SpeedCatalog.ImageSubtitles.FileName ? "PGS subtitles" : step.Spec.FileName);
        return step.Action == FixtureAction.Generating ? "Generating " + name
            : step.Total > 0 ? string.Create(CultureInfo.InvariantCulture, $"Downloading {name}: {step.Done / 1_000_000} of {Math.Round(step.Total / 1_000_000.0):0} MB")
            : "Downloading " + name;
    }

    /// <summary>Makes or downloads the clips the tests need.</summary>
    /// <param name="options">Cache locations and timeouts.</param>
    /// <param name="caps">Build capabilities, for the software encoders.</param>
    /// <param name="tests">The tests.</param>
    /// <param name="settings">The settings, for the subtitles burned in.</param>
    /// <param name="progress">Receives each clip being made or downloaded, or null.</param>
    /// <param name="cancellationToken">Cancels generation.</param>
    /// <returns>Each clip by file name.</returns>
    private async Task<Dictionary<string, FixtureResult>> BuildClipsAsync(EngineOptions options, FfmpegCapabilities caps, IReadOnlyList<SpeedTest> tests, SpeedSettings settings, IProgress<FixtureStep>? progress, CancellationToken cancellationToken)
    {
        var clips = SpeedVariants.Clips(tests, settings);
        var key = Fingerprint.Compute(new FingerprintInputs(options.Ffmpeg.Path, caps.VersionLine, null, null, null, null, null, null));
        Dictionary<string, FixtureResult> built = new(StringComparer.Ordinal);
        foreach (var group in clips.GroupBy(c => c.KeepAcrossBuilds))
        {
            var builder = new FixtureBuilder(_runner, options.Ffmpeg.Path, options.FixturesDirectory, options.FixtureTimeout, FixtureDownloader, [.. group], null) { Progress = progress };
            foreach (var clip in await builder.BuildAsync(group.Key ? SamplesCacheKey : key, caps.Encoders, cancellationToken))
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

    /// <summary>Measures a variant as many times as asked and reports the median.</summary>
    /// <param name="options">The ffmpeg.</param>
    /// <param name="speed">What to measure, for the method and repeats.</param>
    /// <param name="source">The device's argument source.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device.</param>
    /// <param name="test">The test.</param>
    /// <param name="cell">The cell to generate.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The median result, by fps.</returns>
    private async Task<SpeedResult> MeasureRepeatedAsync(EngineOptions options, SpeedOptions speed, IArgumentSource source, HwType type, string device, SpeedTest test, ProbeCell cell, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool TimeUp() => speed.TimeLimit is { } limit && clock.Elapsed >= limit;
        List<SpeedResult> runs = [];
        for (var i = 0; i < Math.Max(1, speed.Repeats); i++)
        {
            var run = await MeasureAsync(options, speed.Method, source, type, device, test, cell, TimeUp, cancellationToken);
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
        return median with { Streams = median.Streams is null ? null : streams };
    }

    /// <summary>Generates one variant's arguments and measures them, inside the probe lock.</summary>
    /// <param name="options">The ffmpeg.</param>
    /// <param name="method">How streams are counted.</param>
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
                    args = source.Build(type, device.Length == 0 ? null : device, cell);
                }
                catch (ArgumentConstructionException)
                {
                    // No hardware arguments at all: Jellyfin would do the whole job in software.
                    return new SpeedResult(type, device, test.Key, string.Empty, null, null, false, test.DecodeOnly ? "Not measured: Jellyfin decodes this in software with this backend." : "Not measured: Jellyfin decodes and encodes this in software with this backend.");
                }
                catch (UnsafeProbeException ex)
                {
                    return new SpeedResult(type, device, test.Key, string.Empty, null, null, false, ex.Message);
                }
                catch (NotSupportedException)
                {
                    // The command-line tool has no subtitle encoder to extract a file's internal text subtitles with.
                    return new SpeedResult(type, device, test.Key, string.Empty, null, null, false, "Burning in a file's own text subtitles needs Jellyfin; measure it from the plugin.");
                }

                // A hardware column needs the step it's about on the GPU: the encode for a transcode, the decode for a decode test. Software has its own column.
                var softwareDecode = type != HwType.none && args.Hwaccel is null && args.HardwareDecoder is null;
                var softwareStep = type == HwType.none ? null
                    : test.DecodeOnly ? (softwareDecode ? "decodes" : null)
                    : args.HardwareEncoder ? null
                    : "encodes";
                if (softwareStep is not null)
                {
                    return new SpeedResult(type, device, test.Key, string.Empty, null, null, false, $"Not measured: Jellyfin {softwareStep} this in software with this backend.");
                }

                var note = softwareDecode ? "Jellyfin decodes this in software with this backend, then encodes on the GPU." : null;
                var width = Math.Min(args.OutputWidth ?? test.Width, test.Width);
                var size = test.DecodeOnly ? null : SpeedTestText.Resolution(width, test.Height * width / test.Width, false);

                string Command(TimeSpan content) => SpeedCommandLine.Build(args, content, test.DecodeOnly, test.StartAt);

                async Task<IReadOnlyList<FfmpegRunResult>> LaunchAsync(int copies, TimeSpan content, CancellationToken token)
                {
                    var invocation = new FfmpegInvocation(options.Ffmpeg.Path, Command(content), args.Environment, copies == 1 ? _singleTimeout : _copiesTimeout);
                    return await Task.WhenAll(Enumerable.Range(0, copies).Select(_ => _runner.RunAsync(invocation, token)));
                }

                var measured = await SpeedMeter.MeasureAsync(LaunchAsync, method, test.FrameRate, !test.DecodeOnly, ct, timeUp);
                return new SpeedResult(type, device, test.Key, string.Empty, measured.Fps, measured.Streams, measured.Capped, measured.Note ?? note) { OutputSize = size };
            },
            cancellationToken);

    /// <summary>Reports clip steps on the caller's thread, in order.</summary>
    /// <param name="report">Applies one step.</param>
    private sealed class StepProgress(Action<FixtureStep> report) : IProgress<FixtureStep>
    {
        /// <inheritdoc/>
        public void Report(FixtureStep value) => report(value);
    }
}
