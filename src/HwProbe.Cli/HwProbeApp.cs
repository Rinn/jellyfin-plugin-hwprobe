using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;
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
            var scrubber = DiagnosticsScrubber.ForCurrentHost((cacheRoot, "<cache>"), (options.FixturesDirectory, "<cache>/fixtures"));
            await DiagnosticsBundle.WriteAsync(options.DiagnosticsPath!, report, recorder.Runs, scrubber, cancellationToken);
            await stderr.WriteLineAsync($"hwprobe: wrote {options.DiagnosticsPath}. Attach it to an issue: {DiagnosticsBundle.IssueUrl}".AsMemory(), cancellationToken);
        }

        var anyViable = report.Backends.Any(b => b.Verdict == BackendVerdict.Viable);
        return options.ExpectHardware && !anyViable ? (int)HwProbeExitCode.NoViableBackend : (int)HwProbeExitCode.Success;
    }
}
