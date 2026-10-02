using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Diagnostics;

/// <summary>Writes a zip of a probe's report and every ffmpeg launch, laid out like <c>tests/Corpus</c>, for attaching to an issue.</summary>
public static partial class DiagnosticsBundle
{
    /// <summary>The GitHub issue form a bundle is attached to.</summary>
    public const string IssueUrl = "https://github.com/Rinn/jellyfin-plugin-hwprobe/issues/new?template=hardware-report.yml";

    private static readonly string[] _listings = ["-version", "-hwaccels", "-encoders", "-decoders", "-filters"];

    /// <summary>Writes a bundle to a file, atomically.</summary>
    /// <param name="path">Destination zip.</param>
    /// <param name="report">The probe's report.</param>
    /// <param name="runs">Every launch the probe made, in order.</param>
    /// <param name="scrubber">Removes identifying paths and names.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public static async Task WriteAsync(string path, CapabilityReport report, IReadOnlyList<RecordedRun> runs, DiagnosticsScrubber scrubber, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = File.Create(temp))
            {
                await WriteAsync(file, report, runs, scrubber, cancellationToken);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>Writes a bundle to a stream.</summary>
    /// <param name="destination">The stream; left open.</param>
    /// <param name="report">The probe's report.</param>
    /// <param name="runs">Every launch the probe made, in order.</param>
    /// <param name="scrubber">Removes identifying paths and names.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the zip is written.</returns>
    public static async Task WriteAsync(Stream destination, CapabilityReport report, IReadOnlyList<RecordedRun> runs, DiagnosticsScrubber scrubber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(scrubber);

        await using var zip = await ZipArchive.CreateAsync(destination, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        await AddAsync(zip, "README.txt", scrubber.Scrub(Readme(report)), cancellationToken);
        await AddAsync(zip, "report.json", scrubber.Scrub(ReportStore.Serialize(report)), cancellationToken);

        var unmatched = report.Probes.Where(p => p.CommandLine is not null).ToList();
        var number = 0;
        foreach (var run in runs)
        {
            if (ListingName(run.Invocation.Arguments) is { } listing)
            {
                await AddAsync(zip, $"ffmpeg/{listing}.txt", scrubber.Scrub(run.Result.Stdout), cancellationToken);
                continue;
            }

            // A probe ID names the file; launches that aren't probes, such as building a test clip, keep a plain name.
            var probe = unmatched.Find(p => string.Equals(p.CommandLine, run.Invocation.Arguments, StringComparison.Ordinal));
            if (probe is not null)
            {
                unmatched.Remove(probe);
            }

            number++;
            var label = probe is null ? "launch" : UnsafeFileCharacters().Replace(probe.ProbeId, "_");
            var name = $"stderr/{number.ToString("D3", CultureInfo.InvariantCulture)}-{label}.txt";
            await AddAsync(zip, name, scrubber.Scrub(Header(run, probe) + run.Result.Stderr), cancellationToken);
        }
    }

    /// <summary>Returns the corpus file name for a capability listing, as <c>ScriptedFfmpegRunner.FromCorpus</c> reads them.</summary>
    /// <param name="arguments">The launch's arguments.</param>
    /// <returns>e.g. <c>hwaccels</c> or <c>h-filter-scale_cuda</c>; null when the launch isn't a listing.</returns>
    private static string? ListingName(string arguments) =>
        _listings.Contains(arguments, StringComparer.Ordinal) ? arguments[1..]
        : arguments.StartsWith("-h filter=", StringComparison.Ordinal) ? "h-filter-" + arguments["-h filter=".Length..]
        : null;

    /// <summary>Describes a launch in <c>#</c> lines, as corpus files start.</summary>
    /// <param name="run">The launch.</param>
    /// <param name="probe">The probe it ran, or null.</param>
    /// <returns>The header, ending in a newline.</returns>
    private static string Header(RecordedRun run, ProbeResult? probe)
    {
        var header = new StringBuilder();
        header.Append(CultureInfo.InvariantCulture, $"# ffmpeg {run.Invocation.Arguments}\n");
        if (run.Invocation.Environment.Count > 0)
        {
            var variables = run.Invocation.Environment.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Value is null ? $"-{e.Key}" : $"{e.Key}={e.Value}");
            header.Append(CultureInfo.InvariantCulture, $"# env: {string.Join(' ', variables)}\n");
        }

        if (probe is not null)
        {
            header.Append(CultureInfo.InvariantCulture, $"# probe: {probe.ProbeId} {probe.Outcome}\n");
        }

        var result = run.Result;
        var ended = result.Status switch
        {
            FfmpegRunStatus.Exited => $"exit {result.ExitCode}" + (result.Frames is { } frames ? $", {frames} frames" : string.Empty),
            FfmpegRunStatus.TimedOut => "timed out",
            _ => $"launch failed: {result.LaunchError}",
        };
        header.Append(CultureInfo.InvariantCulture, $"# result: {ended}, {result.Duration.TotalSeconds:0.00} s\n");
        return header.ToString();
    }

    /// <summary>Returns the README listing what a bundle holds.</summary>
    /// <param name="report">The probe's report.</param>
    /// <returns>The text.</returns>
    private static string Readme(CapabilityReport report) =>
        $"""
        HwProbe diagnostics
        HwProbe {report.HwProbeVersion}, ffmpeg {report.Ffmpeg.Version} ({report.Ffmpeg.Path}), {report.Host.Os} {report.Host.Kernel} {report.Host.Architecture}, made {report.GeneratedUtc:u}.

        To report results, attach this zip to an issue: {IssueUrl}

        report.json  The probe report.
        ffmpeg/      What this ffmpeg was built with: -version, -hwaccels, -encoders, -decoders, -filters, and the -h filter= pages Jellyfin reads.
        stderr/      Every ffmpeg launch in order: its arguments, environment and result, then its complete output.

        Home and cache directories, the user name and the host name are replaced with ~, <cache>, <user> and <host>. Device names, driver versions and file paths outside those directories are kept. Look through the files before sharing them.

        """;

    /// <summary>Adds a text entry.</summary>
    /// <param name="zip">The archive.</param>
    /// <param name="name">The entry name.</param>
    /// <param name="text">The contents.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the entry is written.</returns>
    private static async Task AddAsync(ZipArchive zip, string name, string text, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    /// <summary>Matches characters not allowed in file names on every OS.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeFileCharacters();
}
