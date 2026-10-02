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

            await stderr.WriteLineAsync($"hwprobe: wrote {options.DiagnosticsPath}. Attach it to an issue: {DiagnosticsBundle.IssueUrl}".AsMemory(), cancellationToken);
        }

        if (options.Speed is { } requested)
        {
            var speed = requested;
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

                // Without named file tests, measure the file's own transcodes as well as the chosen ones.
                var fileTests = SpeedFileTests.For(file).Select(t => t.Key).ToList();
                speed = speed with { Tests = speed.Tests.Any(t => fileTests.Contains(t)) ? speed.Tests : [.. speed.Tests, .. fileTests], File = file };
            }

            var viable = report.Backends.Where(b => b.Verdict == BackendVerdict.Viable).Select(b => (b.Type, b.Device)).ToList();
            using var engine = new SpeedEngine(new FfmpegRunner(), new ArgumentSourceFactory(), platform, TimeProvider.System, EnvironmentRules.Standalone());
            var progress = new Progress<(int Done, int Total)>(p => stderr.Write($"\rhwprobe: speed {p.Done} of {p.Total}"));
            var measured = await engine.RunAsync(engineOptions, speed, viable, progress, cancellationToken);
            await stderr.WriteLineAsync(string.Empty.AsMemory(), cancellationToken);
            if (options.Format != OutputFormat.Json)
            {
                await stdout.WriteAsync(SpeedRenderer.Render(measured).AsMemory(), cancellationToken);
            }

            if (options.SpeedJsonPath is not null)
            {
                await SpeedReportStore.WriteAsync(measured, options.SpeedJsonPath, cancellationToken);
            }
        }

        var anyViable = report.Backends.Any(b => b.Verdict == BackendVerdict.Viable);
        return options.ExpectHardware && !anyViable ? (int)HwProbeExitCode.NoViableBackend : (int)HwProbeExitCode.Success;
    }
}
