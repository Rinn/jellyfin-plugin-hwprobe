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

        var ffmpeg = options.Ffmpeg.Path;
        var caps = await new FfmpegCapabilityProbe(_runner, options.ProbeTimeout).ProbeAsync(ffmpeg, cancellationToken);
        if (caps.Validation != FfmpegValidation.Valid)
        {
            throw new FfmpegUnusableException($"{ffmpeg}: {caps.Validation}.");
        }

        var host = new HostInfoReader(_platform).Read();
        var tests = speed.Resolve();
        var clips = await BuildClipsAsync(options, caps, tests, cancellationToken);

        List<(HwType Type, string Device)> measured = [.. backends.Where(b => b.Type != HwType.none), (HwType.none, string.Empty)];
        var total = measured.Sum(b => tests.Sum(t => 1 + (MissingClip(t, clips) is null ? SpeedVariants.For(b.Type, t, SpeedVariants.Base(t, speed.Settings, Placeholders(t)), speed.Comparisons).Count() : 0)));
        var done = 0;
        List<SpeedResult> results = [];
        foreach (var (type, device) in measured)
        {
            var traits = await OpenAsync(options, type, device, host.Os, cancellationToken);
            var source = traits is null ? null : _arguments.Create(caps, traits);
            foreach (var test in tests)
            {
                var missing = MissingClip(test, clips);
                var cell = missing is null ? SpeedVariants.Base(test, speed.Settings, Paths(clips)) : null;
                List<(string Label, ProbeCell? Cell)> runs = [(string.Empty, cell), .. cell is null ? [] : SpeedVariants.For(type, test, cell, speed.Comparisons).Select(v => (v.Label, (ProbeCell?)v.Cell))];
                string? baseCommand = null;
                foreach (var (label, variant) in runs)
                {
                    var result = source is null ? new SpeedResult(type, device, test.Key, label, null, null, false, "The device didn't open.")
                        : variant is null ? new SpeedResult(type, device, test.Key, label, null, null, false, missing)
                        : await MeasureAsync(options, speed.Method, source, type, device, test, label, variant, baseCommand, c => baseCommand ??= c, cancellationToken);
                    var described = result with { Label = test.Label, Video = test.Name, Output = test.OutputLabel, Input = SpeedTestText.Input(test), FrameRate = test.FrameRate, Credit = test.Credit, LicenseUrl = test.LicenseUrl };
                    results.Add(described);
                    progress?.Report(new SpeedProgress(++done, total, described));
                }
            }
        }

        return new SpeedReport(
            _time.GetUtcNow(),
            new FfmpegSummary(ffmpeg, options.Ffmpeg.Source.ToString(), caps.Version?.ToString() ?? "unknown", caps.IsJellyfinBuild),
            speed.Method,
            results)
        {
            Settings = speed.Settings,
        };
    }

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

    /// <summary>Returns why a test's clips aren't available, or null when they are.</summary>
    /// <param name="test">The test.</param>
    /// <param name="clips">Every clip built.</param>
    /// <returns>The reason, or null.</returns>
    private static string? MissingClip(SpeedTest test, Dictionary<string, FixtureResult> clips) =>
        SpeedVariants.Clips([test]).Select(f => clips[f.FileName]).FirstOrDefault(c => c.Status != FixtureStatus.Available) is { } missing
            ? $"No {missing.Spec.FileName} clip: {missing.Reason}"
            : null;

    /// <summary>Returns available clip paths by file name.</summary>
    /// <param name="clips">Every clip built.</param>
    /// <returns>The paths.</returns>
    private static Dictionary<string, string> Paths(Dictionary<string, FixtureResult> clips) =>
        clips.Values.Where(c => c.Path is not null).ToDictionary(c => c.Spec.FileName, c => c.Path!, StringComparer.Ordinal);

    /// <summary>Returns stand-in paths for a test's clips, for counting its comparisons before any clip exists.</summary>
    /// <param name="test">The test.</param>
    /// <returns>Each clip's file name, as its own path.</returns>
    private static Dictionary<string, string> Placeholders(SpeedTest test) =>
        SpeedVariants.Clips([test]).ToDictionary(f => f.FileName, f => f.FileName, StringComparer.Ordinal);

    /// <summary>Makes or downloads the clips the tests need.</summary>
    /// <param name="options">Cache locations and timeouts.</param>
    /// <param name="caps">Build capabilities, for the software encoders.</param>
    /// <param name="tests">The tests.</param>
    /// <param name="cancellationToken">Cancels generation.</param>
    /// <returns>Each clip by file name.</returns>
    private async Task<Dictionary<string, FixtureResult>> BuildClipsAsync(EngineOptions options, FfmpegCapabilities caps, IReadOnlyList<SpeedTest> tests, CancellationToken cancellationToken)
    {
        var clips = SpeedVariants.Clips(tests);
        var key = Fingerprint.Compute(new FingerprintInputs(options.Ffmpeg.Path, caps.VersionLine, null, null, null, null, null, null));
        Dictionary<string, FixtureResult> built = new(StringComparer.Ordinal);
        foreach (var group in clips.GroupBy(c => c.KeepAcrossBuilds))
        {
            var builder = new FixtureBuilder(_runner, options.Ffmpeg.Path, options.FixturesDirectory, options.FixtureTimeout, FixtureDownloader, [.. group], null);
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

    /// <summary>Generates one variant's arguments and measures them, inside the probe lock.</summary>
    /// <param name="options">The ffmpeg.</param>
    /// <param name="method">How streams are counted.</param>
    /// <param name="source">The device's argument source.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device.</param>
    /// <param name="test">The test.</param>
    /// <param name="label">The comparison label, or empty for the base.</param>
    /// <param name="cell">The cell to generate.</param>
    /// <param name="baseCommand">The base variant's command, to skip a comparison that changes nothing; null for the base.</param>
    /// <param name="remember">Records the base variant's command.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The result.</returns>
    private Task<SpeedResult> MeasureAsync(
        EngineOptions options,
        SpeedMethod method,
        IArgumentSource source,
        HwType type,
        string device,
        SpeedTest test,
        string label,
        ProbeCell cell,
        string? baseCommand,
        Action<string> remember,
        CancellationToken cancellationToken) =>
        _gate.RunAsync(
            async ct =>
            {
                ProbeArguments args;
                try
                {
                    args = source.Build(type, device.Length == 0 ? null : device, cell);
                }
                catch (Exception ex) when (ex is ArgumentConstructionException or UnsafeProbeException)
                {
                    return new SpeedResult(type, device, test.Key, label, null, null, false, test.DecodeOnly ? "Jellyfin decodes this in software." : ex.Message);
                }
                catch (NotSupportedException)
                {
                    // The command-line tool has no subtitle encoder to extract a file's internal text subtitles with.
                    return new SpeedResult(type, device, test.Key, label, null, null, false, "Burning in a file's own text subtitles needs Jellyfin; measure it from the plugin.");
                }

                // Software decodes and encodes everything, so only a hardware backend can fall back.
                var note = type == HwType.none ? null
                    : args.Hwaccel is null && args.HardwareDecoder is null ? "Jellyfin decodes this in software."
                    : !test.DecodeOnly && !args.HardwareEncoder ? "Jellyfin encodes this in software."
                    : null;
                string Command(TimeSpan content) => SpeedCommandLine.Build(args, content, test.DecodeOnly, test.StartAt);

                var command = Command(SpeedMeter.Content);
                if (label.Length == 0)
                {
                    remember(command);
                }
                else if (string.Equals(command, baseCommand, StringComparison.Ordinal))
                {
                    return new SpeedResult(type, device, test.Key, label, null, null, false, "Jellyfin passes the same arguments with this setting.");
                }

                async Task<IReadOnlyList<FfmpegRunResult>> LaunchAsync(int copies, TimeSpan content, CancellationToken token)
                {
                    var invocation = new FfmpegInvocation(options.Ffmpeg.Path, Command(content), args.Environment, copies == 1 ? _singleTimeout : _copiesTimeout);
                    return await Task.WhenAll(Enumerable.Range(0, copies).Select(_ => _runner.RunAsync(invocation, token)));
                }

                var measured = await SpeedMeter.MeasureAsync(LaunchAsync, method, test.FrameRate, !test.DecodeOnly, ct);
                return new SpeedResult(type, device, test.Key, label, measured.Fps, measured.Streams, measured.Capped, measured.Note ?? note);
            },
            cancellationToken);
}
