using System.Globalization;
using System.Text;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Renders a report as a plain-text table.</summary>
internal static class TableRenderer
{
    private static readonly string[] _headers = ["TYPE", "DEVICE", "VERDICT", "TIER", "DECODE", "ENCODE", "TONEMAP"];

    /// <summary>Renders the report.</summary>
    /// <param name="report">The report.</param>
    /// <param name="verbose">Also list every probe with its stderr tail.</param>
    /// <returns>The table text, newline-terminated.</returns>
    public static string Render(CapabilityReport report, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        var ffmpeg = report.Ffmpeg;
        text.Append(CultureInfo.InvariantCulture, $"ffmpeg  {ffmpeg.Path} ({ffmpeg.Version}, {ffmpeg.Source})\n");
        if (!ffmpeg.IsJellyfinBuild)
        {
            text.Append("        warning: not a jellyfin-ffmpeg build; results may differ from the server's\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"host    {report.Host.Os} {report.Host.Kernel}{(report.Host.Container is { } c ? $" ({c})" : string.Empty)}\n");
        var notBuilt = report.StageA.Types.Where(t => t.Value == BuildStatus.NotBuilt).Select(t => t.Key.ToString()).Order(StringComparer.Ordinal);
        text.Append(CultureInfo.InvariantCulture, $"build   not built: {Or(string.Join(", ", notBuilt), "none")}\n\n");

        List<string[]> rows = [_headers, .. report.Backends.Select(Row)];
        var widths = Enumerable.Range(0, _headers.Length).Select(i => rows.Max(r => r[i].Length)).ToArray();
        foreach (var row in rows)
        {
            text.Append(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd()).Append('\n');
        }

        foreach (var backend in report.Backends.Where(b => !string.IsNullOrEmpty(b.Hint)))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{backend.Type} {Or(backend.Device, "-")}: {backend.Hint}");
        }

        if (report.Backends.Any(b => !string.IsNullOrEmpty(b.Hint)))
        {
            text.Append('\n');
        }

        foreach (var finding in report.Findings)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{finding.Severity.ToString().ToUpperInvariant()} {finding.Code}: {finding.Message}");
        }

        if (report.Findings.Count > 0)
        {
            text.Append('\n');
        }

        if (verbose)
        {
            foreach (var probe in report.Probes)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n[{probe.Outcome}] {probe.ProbeId} ({probe.Duration.TotalSeconds:0.0}s)\n");
                if (probe.CommandLine is not null)
                {
                    text.Append(CultureInfo.InvariantCulture, $"$ {ffmpeg.Path} {probe.CommandLine}\n");
                }

                if (!string.IsNullOrEmpty(probe.Hint))
                {
                    text.Append(CultureInfo.InvariantCulture, $"hint: {probe.Hint}\n");
                }

                if (!string.IsNullOrEmpty(probe.StderrTail))
                {
                    text.Append(probe.StderrTail.TrimEnd()).Append('\n');
                }
            }
        }

        return text.ToString();
    }

    /// <summary>Formats one backend row.</summary>
    /// <param name="backend">The backend.</param>
    /// <returns>Cells in header order.</returns>
    private static string[] Row(BackendReport backend) =>
    [
        backend.Type.ToString(),
        Or(backend.Device, "-"),
        backend.Verdict.ToString(),
        backend.Verdict == BackendVerdict.Viable ? backend.Tier.ToString() : "-",
        Cells(backend.Decode),
        Cells(backend.Encode),
        Cells(backend.Tonemap),
    ];

    /// <summary>Summarises a cell map as <c>passed/tested</c> plus the failing keys.</summary>
    /// <param name="cells">Cells by key.</param>
    /// <returns>The summary, or <c>-</c> when empty.</returns>
    private static string Cells(IReadOnlyDictionary<string, ProbeOutcome> cells)
    {
        var tested = cells.Where(c => c.Value is not (ProbeOutcome.Skipped or ProbeOutcome.Untested)).ToList();
        if (tested.Count == 0)
        {
            return "-";
        }

        var failed = tested.Where(c => c.Value != ProbeOutcome.Pass).Select(c => c.Key).Order(StringComparer.Ordinal).ToList();
        var summary = $"{tested.Count - failed.Count}/{tested.Count}";
        return failed.Count == 0 ? summary : $"{summary} (no {string.Join(",", failed)})";
    }

    /// <summary>Returns the value, or a fallback when it is empty.</summary>
    /// <param name="value">The value.</param>
    /// <param name="fallback">Used when the value is empty.</param>
    /// <returns>The value or fallback.</returns>
    private static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;
}
