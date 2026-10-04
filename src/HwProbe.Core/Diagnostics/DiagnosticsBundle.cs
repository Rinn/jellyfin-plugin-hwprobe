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
    private static readonly string[] _listings = ["-version", "-hwaccels", "-encoders", "-decoders", "-filters"];

    /// <summary>Writes a bundle to a file, atomically.</summary>
    /// <param name="path">Destination zip.</param>
    /// <param name="report">The probe's report.</param>
    /// <param name="runs">Every launch the probe made, in order.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public static async Task WriteAsync(string path, CapabilityReport report, IReadOnlyList<RecordedRun> runs, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = File.Create(temp))
            {
                await WriteAsync(file, report, runs, cancellationToken);
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
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the zip is written.</returns>
    public static async Task WriteAsync(Stream destination, CapabilityReport report, IReadOnlyList<RecordedRun> runs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(runs);

        await using var zip = await ZipArchive.CreateAsync(destination, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        await AddAsync(zip, "README.txt", Readme(report), cancellationToken);
        await AddAsync(zip, "report.json", ReportStore.Serialize(report), cancellationToken);

        var unmatched = report.Probes.Where(p => p.CommandLine is not null).ToList();
        var number = 0;
        foreach (var run in runs)
        {
            if (ListingName(run.Invocation.Arguments) is { } listing)
            {
                await AddAsync(zip, $"ffmpeg/{listing}.txt", run.Result.Stdout, cancellationToken);
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
            await AddAsync(zip, name, Header(run, probe) + run.Result.Stderr, cancellationToken);
        }
    }

    /// <summary>Reads the report a bundle holds.</summary>
    /// <param name="zip">The bundle.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The report, or null when the zip or its report can't be read.</returns>
    public static async Task<CapabilityReport?> ReadReportAsync(byte[] zip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zip);
        try
        {
            using var stream = new MemoryStream(zip, writable: false);
            await using var archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null, cancellationToken);
            if (archive.GetEntry("report.json") is not { } entry)
            {
                return null;
            }

            using var reader = new StreamReader(await entry.OpenAsync(cancellationToken));
            return ReportStore.Deserialize(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Returns a copy of a bundle with a text file added, such as log lines read when it's downloaded.</summary>
    /// <param name="zip">The bundle.</param>
    /// <param name="name">The file's name in the zip.</param>
    /// <param name="text">Its contents.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The new bundle.</returns>
    public static async Task<byte[]> WithFileAsync(byte[] zip, string name, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zip);
        using var stream = new MemoryStream();
        await stream.WriteAsync(zip, cancellationToken);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(name)?.Delete();
            await AddAsync(archive, name, text, cancellationToken);
        }

        return stream.ToArray();
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

        // The runner's own variables come first, as it sets them, so the header shows the environment the stderr came from.
        var environment = FfmpegRunner.BaseEnvironment.Where(e => !run.Invocation.Environment.ContainsKey(e.Key)).Select(e => KeyValuePair.Create(e.Key, (string?)e.Value)).Concat(run.Invocation.Environment).ToList();
        if (environment.Count > 0)
        {
            var variables = environment.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Value is null ? $"-{e.Key}" : $"{e.Key}={e.Value}");
            header.Append(CultureInfo.InvariantCulture, $"# env: {string.Join(' ', variables)}\n");
        }

        if (probe is not null)
        {
            header.Append(CultureInfo.InvariantCulture, $"# probe: {probe.ProbeId} {probe.Outcome}\n");
        }

        var result = run.Result;
        var ended = result.Status switch
        {
            FfmpegRunStatus.Exited => string.Create(CultureInfo.InvariantCulture, $"exit {result.ExitCode}") + (result.Frames is { } frames ? string.Create(CultureInfo.InvariantCulture, $", {frames} frames") : string.Empty),
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

        To report results, attach this zip to an issue: {IssueLink.For(report, null)}

        report.json  The probe report.
        ffmpeg/      What this ffmpeg was built with: -version, -hwaccels, -encoders, -decoders, -filters, and the -h filter= pages Jellyfin reads.
        stderr/      Every ffmpeg launch in order: its arguments, environment and result, then its complete output.

        Nothing has been removed: file paths, user and host names, device names and driver versions appear as ffmpeg and HwProbe saw them.

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
