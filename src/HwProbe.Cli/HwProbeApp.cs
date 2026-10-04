using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.Jellyfin;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Runs a probe for parsed options and renders the report.</summary>
internal static class HwProbeApp
{
    /// <summary>Runs hwprobe.</summary>
    /// <param name="options">Parsed options.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl+C.</param>
    /// <returns>A <see cref="HwProbeExitCode"/> value.</returns>
    public static Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken) =>
        RunAsync(options, new FfmpegLocator(), new HostPlatform(), CacheDirectory.Resolve(), Console.Out, Console.Error, cancellationToken);

    /// <summary>Runs hwprobe with injected host access and output streams.</summary>
    /// <param name="options">Parsed options.</param>
    /// <param name="locator">Finds ffmpeg.</param>
    /// <param name="platform">Host access.</param>
    /// <param name="cacheRoot">Root for cached reports.</param>
    /// <param name="stdout">Report output.</param>
    /// <param name="stderr">Diagnostics.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl+C.</param>
    /// <returns>A <see cref="HwProbeExitCode"/> value.</returns>
    public static async Task<int> RunAsync(CliOptions options, FfmpegLocator locator, IHostPlatform platform, string cacheRoot, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        FfmpegLocation? location;
        try
        {
            location = locator.Locate(options.FfmpegPath);
        }
        catch (FileNotFoundException ex)
        {
            await stderr.WriteLineAsync($"hwprobe: {ex.Message}".AsMemory(), cancellationToken);
            return (int)HwProbeExitCode.FfmpegUnusable;
        }

        if (location is null)
        {
            await stderr.WriteLineAsync("hwprobe: ffmpeg not found. Pass --ffmpeg or set JELLYFIN_FFMPEG.".AsMemory(), cancellationToken);
            return (int)HwProbeExitCode.FfmpegUnusable;
        }

        // A cached report has no ffmpeg logs to bundle.
        var refresh = options.Refresh || options.DiagnosticsPath is not null;
        var engineOptions = new EngineOptions(
            location,
            options.StopAfter,
            options.Types,
            options.Device,
            options.ProbeTimeout,
            options.FixtureTimeout,
            options.FixturesDirectory,
            Path.Combine(cacheRoot, "reports"),
            refresh);

        var recorder = options.DiagnosticsPath is null ? null : new RecordingFfmpegRunner(new FfmpegRunner());
        CapabilityReport report;
        try
        {
            using var engine = new ProbeEngine((IFfmpegRunner?)recorder ?? new FfmpegRunner(), new ArgumentSourceFactory(), platform, TimeProvider.System, EnvironmentRules.Standalone());
            report = await engine.RunAsync(engineOptions, cancellationToken);
        }
        catch (FfmpegUnusableException ex)
        {
            await stderr.WriteLineAsync($"hwprobe: unusable ffmpeg: {ex.Message}".AsMemory(), cancellationToken);
            return (int)HwProbeExitCode.FfmpegUnusable;
        }

        var rendered = options.Format switch
        {
            OutputFormat.Json => ReportStore.Serialize(report) + "\n",
            OutputFormat.Summary => SummaryRenderer.Render(report),
            _ => TableRenderer.Render(report, options.Verbose),
        };
        await stdout.WriteAsync(rendered.AsMemory(), cancellationToken);
        if (options.JsonPath is not null)
        {
            await ReportStore.WriteAsync(report, options.JsonPath, cancellationToken);
        }

        if (recorder is not null)
        {
            try
            {
                await DiagnosticsBundle.WriteAsync(options.DiagnosticsPath!, report, recorder.Runs, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await stderr.WriteLineAsync($"hwprobe: couldn't write {options.DiagnosticsPath}: {ex.Message}".AsMemory(), cancellationToken);
                return (int)HwProbeExitCode.InternalError;
            }

            await stderr.WriteLineAsync($"hwprobe: wrote {options.DiagnosticsPath}. Attach it to an issue: {IssueLink.For(report, null)}".AsMemory(), cancellationToken);
        }

        if (options.Speed is { } requested)
        {
            var speed = requested;
            if (options.SpeedFilePath is null && speed.Videos.Contains(SpeedCatalog.LibraryKey))
            {
                await stderr.WriteLineAsync("hwprobe: the library speed video requires --speed-file.".AsMemory(), cancellationToken);
                return (int)HwProbeExitCode.UsageError;
            }

            if (options.SpeedFilePath is { } path)
            {
                SpeedFile file;
                try
                {
                    file = await FfprobeFile.ReadAsync(location.Path, path, cancellationToken);
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
                {
                    await stderr.WriteLineAsync($"hwprobe: {ex.Message}".AsMemory(), cancellationToken);
                    return (int)HwProbeExitCode.UsageError;
                }

                // A given file is measured even when --speed-videos doesn't name it.
                speed = speed with { Videos = speed.Videos.Contains(SpeedCatalog.LibraryKey) ? speed.Videos : [.. speed.Videos, SpeedCatalog.LibraryKey], File = file };
            }

            var viable = report.Backends.Where(b => b.Verdict == BackendVerdict.Viable).Select(b => (b.Type, b.Device)).ToList();
            using var engine = new SpeedEngine(new FfmpegRunner(), new ArgumentSourceFactory(), platform, TimeProvider.System, EnvironmentRules.Standalone());

            // A terminal gets one line rewritten in place; a log (Docker, a pipe) gets a line each time the status changes.
            var lastStatus = string.Empty;
            var progress = new DirectProgress<SpeedProgress>(p =>
            {
                var status = p.Preparing is { } preparing ? $"hwprobe: speed: {preparing}" : string.Create(CultureInfo.InvariantCulture, $"hwprobe: speed {p.Done} of {p.Total}");
                if (status == lastStatus)
                {
                    return;
                }

                lastStatus = status;
                stderr.Write(Console.IsErrorRedirected ? status + Environment.NewLine : "\r" + status);
            });

            var lowPower = SpeedOptions.MissingLowPower(report);
            if (options.Suite is { } key)
            {
                var suite = Catalog.Default.Suites.First(s => s.Key == key);

                // The hardware backend comes from --speed-backends, or else the first working one in Jellyfin's dropdown order, QSV in place of VAAPI on the same GPU.
                var order = Catalog.Default.Backends.Select(b => b.Type).ToList();
                var candidates = viable.Where(v => v.Type != HwType.none && (speed.Backends is null || speed.Backends.Contains(v.Type))).OrderBy(v => order.IndexOf(v.Type)).ToList();
                var type = candidates.Count == 0 ? HwType.none : BackendPreference.Prefer(candidates[0], candidates).Type;
                if (!SpeedSuites.Offered(suite, report, type))
                {
                    await stderr.WriteLineAsync($"hwprobe: the {suite.Name} suite can't run with the backends that work here.".AsMemory(), cancellationToken);
                    return (int)HwProbeExitCode.UsageError;
                }

                var started = DateTimeOffset.UtcNow;
                var backends = SpeedSuites.Backends(suite, type);

                // Without a server, Jellyfin's defaults (and any --speed-option) stand for its settings.
                var steps = SpeedSuites.Steps(suite, Environment.ProcessorCount, speed.Settings, type);
                List<SpeedReport> reports = [];

                // A stop between steps ends the suite there; what finished is still printed, so these writes aren't cancelled with it.
                foreach (var step in steps.TakeWhile(_ => !cancellationToken.IsCancellationRequested))
                {
                    var settings = step.Options.Aggregate(speed.Settings, (current, option) => SpeedSettingsOptions.Apply(current, option.Key, option.Value) ?? current);
                    await stderr.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"hwprobe: {suite.Name}: {step.Label}, {reports.Count + 1} of {steps.Count}").AsMemory(), CancellationToken.None);
                    var stepReport = await engine.RunAsync(engineOptions, speed with { Videos = step.Videos, Outputs = step.Outputs, Settings = settings, Backends = step.HardwareOnly ? [.. backends.Where(b => b != HwType.none)] : backends, LowPowerUnsupported = lowPower }, viable, progress, cancellationToken);
                    reports.Add(stepReport with { Suite = suite.Name, SuiteStep = step.Label, SuiteStartedUtc = started });
                    if (stepReport.Cancelled)
                    {
                        break;
                    }
                }

                await stderr.WriteLineAsync(string.Empty.AsMemory(), CancellationToken.None);
                if (reports.Count < steps.Count)
                {
                    await stderr.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"hwprobe: {suite.Name} stopped after {reports.Count(r => !r.Cancelled)} of {steps.Count} steps: {StopReason.Describe()}").AsMemory(), CancellationToken.None);
                }

                if (options.Format != OutputFormat.Json)
                {
                    foreach (var stepReport in reports)
                    {
                        await stdout.WriteAsync($"\nstep    {stepReport.SuiteStep}\n{SpeedRenderer.Render(stepReport)}".AsMemory(), CancellationToken.None);
                    }

                    await stdout.WriteAsync(SuiteRenderer.Render(suite.Name, reports).AsMemory(), CancellationToken.None);
                    var device = candidates.FirstOrDefault(c => c.Type == type).Device ?? string.Empty;
                    if (reports.Count > 0)
                    {
                        await stdout.WriteAsync(SuggestionRenderer.Render(SpeedAdvisor.Advise(reports[^1], reports, type, device, speed.Settings)).AsMemory(), CancellationToken.None);
                    }
                }

                if (options.SpeedJsonPath is { } suiteJson)
                {
                    await File.WriteAllTextAsync(suiteJson, "[" + string.Join(",", reports.Select(SpeedReportStore.Serialize)) + "]\n", CancellationToken.None);
                }
            }
            else
            {
                var measured = await engine.RunAsync(engineOptions, speed with { LowPowerUnsupported = lowPower }, viable, progress, cancellationToken);

                // A cancelled run still returns what it finished, so its output isn't cancelled with it.
                await stderr.WriteLineAsync(string.Empty.AsMemory(), CancellationToken.None);
                if (measured.Cancelled)
                {
                    await stderr.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"hwprobe: speed test stopped after {measured.Results.Count} measurements: {StopReason.Describe()}").AsMemory(), CancellationToken.None);
                }

                if (options.Format != OutputFormat.Json)
                {
                    await stdout.WriteAsync(SpeedRenderer.Render(measured).AsMemory(), CancellationToken.None);
                }

                if (options.SpeedJsonPath is not null)
                {
                    await SpeedReportStore.WriteAsync(measured, options.SpeedJsonPath, CancellationToken.None);
                }
            }
        }

        var anyViable = report.Backends.Any(b => b.Verdict == BackendVerdict.Viable);
        return options.ExpectHardware && !anyViable ? (int)HwProbeExitCode.NoViableBackend : (int)HwProbeExitCode.Success;
    }
}
