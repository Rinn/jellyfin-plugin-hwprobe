using System.Globalization;
using System.Text;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Verdict;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Renders a report as a compact summary meant for pasting back, with only the stderr lines that matter.</summary>
internal static class SummaryRenderer
{
    private const int MaxExplainingLines = 3;
    private const int MaxLineLength = 160;

    /// <summary>Renders the report.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The summary text, newline-terminated.</returns>
    public static string Render(CapabilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        var ffmpeg = report.Ffmpeg;
        var build = ffmpeg.IsJellyfinBuild ? "jellyfin-ffmpeg" : "NOT jellyfin-ffmpeg";
        Line(text, $"hwprobe summary (report schema {report.SchemaVersion})");
        Line(text, $"ffmpeg  {ffmpeg.Version}, {build}, {ffmpeg.Path} ({ffmpeg.Source})");
        Line(text, $"host    {report.Host.Os} {report.Host.Kernel}{(report.Host.Container is { } c ? $", in {c}" : string.Empty)}");
        Line(text, $"built   {Join(report.StageA.Types, BuildStatus.Selectable)}; not built: {Join(report.StageA.Types, BuildStatus.NotBuilt)}");

        // Long remedies repeat across failures, so each is printed once and referenced by number.
        var remedies = new List<string>();
        foreach (var backend in report.Backends)
        {
            text.Append('\n');
            var tier = backend.Verdict == BackendVerdict.Viable ? $", tier {backend.Tier}" : string.Empty;
            Line(text, $"{backend.Type} {Or(backend.Device)}: {backend.Verdict}{tier}{RemedyRef(remedies, backend.Hint)}");
            Cells(text, "decode", backend.Decode);
            Cells(text, "encode", backend.Encode);
            Cells(text, "tonemap", backend.Tonemap);
            Cells(text, "deint", backend.Deinterlace);
            Cells(text, "subs", backend.Subtitles);
        }

        if (report.Findings.Count > 0)
        {
            text.Append("\nfindings\n");
            foreach (var finding in report.Findings)
            {
                Line(text, $"  {finding.Severity.ToString().ToUpperInvariant()} {finding.Code}: {finding.Message}");
            }
        }

        var failures = report.Probes.Where(p => p.Outcome is not (ProbeOutcome.Pass or ProbeOutcome.Skipped)).ToList();
        if (failures.Count > 0)
        {
            text.Append("\nfailures\n");
            foreach (var probe in failures)
            {
                Line(text, $"  {probe.ProbeId}: {probe.Outcome}{RemedyRef(remedies, probe.Hint)}");
                foreach (var line in ExplainingLines(probe.StderrTail))
                {
                    Line(text, $"    > {line}");
                }
            }
        }

        if (remedies.Count > 0)
        {
            text.Append("\nremedies\n");
            for (var i = 0; i < remedies.Count; i++)
            {
                Line(text, $"  [{i + 1}] {remedies[i]}");
            }
        }

        return text.ToString();
    }

    /// <summary>Appends one line.</summary>
    /// <param name="text">The buffer.</param>
    /// <param name="line">The line.</param>
    private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');

    /// <summary>Appends a cell group as passed, failed (with outcome), skipped and untested keys.</summary>
    /// <param name="text">The buffer.</param>
    /// <param name="name">Group label.</param>
    /// <param name="cells">Cells by key.</param>
    private static void Cells(StringBuilder text, string name, IReadOnlyDictionary<string, ProbeOutcome> cells)
    {
        if (cells.Count == 0)
        {
            return;
        }

        var parts = new List<string>();
        var ok = Keys(cells, o => o == ProbeOutcome.Pass);
        var failed = cells.Where(c => c.Value is not (ProbeOutcome.Pass or ProbeOutcome.Skipped or ProbeOutcome.Untested))
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => $"{c.Key}({c.Value})")
            .ToList();
        var skipped = Keys(cells, o => o == ProbeOutcome.Skipped);
        var untested = Keys(cells, o => o == ProbeOutcome.Untested);
        if (ok.Length > 0)
        {
            parts.Add($"ok: {ok}");
        }

        if (failed.Count > 0)
        {
            parts.Add($"no: {string.Join(' ', failed)}");
        }

        if (skipped.Length > 0)
        {
            parts.Add($"skipped: {skipped}");
        }

        if (untested.Length > 0)
        {
            parts.Add($"untested: {untested}");
        }

        Line(text, string.Create(CultureInfo.InvariantCulture, $"  {name,-8}{string.Join(" | ", parts)}"));
    }

    /// <summary>Returns the keys whose outcome matches, space-separated in key order.</summary>
    /// <param name="cells">Cells by key.</param>
    /// <param name="match">Outcome filter.</param>
    /// <returns>The keys.</returns>
    private static string Keys(IReadOnlyDictionary<string, ProbeOutcome> cells, Func<ProbeOutcome, bool> match) =>
        string.Join(' ', cells.Where(c => match(c.Value)).Select(c => c.Key).Order(StringComparer.Ordinal));

    /// <summary>Returns the stderr lines containing a failure marker, deduplicated and shortened.</summary>
    /// <param name="stderr">The stderr tail.</param>
    /// <returns>Up to three lines.</returns>
    private static IEnumerable<string> ExplainingLines(string stderr) =>
        stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => StderrMarkers.AllFailures.Any(m => l.Contains(m, StringComparison.Ordinal))
                && !StderrMarkers.Harmless.Any(m => l.Contains(m, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxExplainingLines)
            .Select(l => l.Length > MaxLineLength ? l[..MaxLineLength] + "…" : l);

    /// <summary>Registers a remedy and returns its reference, or nothing when there's no remedy.</summary>
    /// <param name="remedies">Remedies so far.</param>
    /// <param name="hint">The remedy text.</param>
    /// <returns><c> [n]</c>, or empty.</returns>
    private static string RemedyRef(List<string> remedies, string hint)
    {
        if (string.IsNullOrEmpty(hint))
        {
            return string.Empty;
        }

        var index = remedies.IndexOf(hint);
        if (index < 0)
        {
            remedies.Add(hint);
            index = remedies.Count - 1;
        }

        return string.Create(CultureInfo.InvariantCulture, $" [{index + 1}]");
    }

    /// <summary>Returns backends with a build status, space-separated.</summary>
    /// <param name="types">Build status by backend.</param>
    /// <param name="status">The status to list.</param>
    /// <returns>The backends, or <c>none</c>.</returns>
    private static string Join(IReadOnlyDictionary<HwType, BuildStatus> types, BuildStatus status)
    {
        var names = string.Join(' ', types.Where(t => t.Value == status).Select(t => t.Key.ToString()).Order(StringComparer.Ordinal));
        return names.Length == 0 ? "none" : names;
    }

    /// <summary>Returns the device, or <c>-</c> when there is none.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The display value.</returns>
    private static string Or(string device) => device.Length == 0 ? "-" : device;
}
