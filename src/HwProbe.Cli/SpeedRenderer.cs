using System.Globalization;
using System.Text;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Renders a speed report as plain text: a summary per transcode, then a table.</summary>
internal static class SpeedRenderer
{
    // Runs of the same command vary by a few percent, so smaller differences aren't shown as one.
    private const double Noise = 0.05;

    private static readonly string[] _headers = ["TYPE", "DEVICE", "TEST", "COMPARED", "STREAMS", "SPEED", "FPS", "CHANGE", "NOTE"];

    /// <summary>Renders the report.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The text, newline-terminated.</returns>
    public static string Render(SpeedReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"\nspeed   {report.Method} method. Streams: transcodes kept at real time at once. Speed: one alone, as a multiple of real time.\n        Change: speed against the base settings. Test-pattern clips encode faster than real video.\n\n");

        var bases = report.Results.Where(r => r.Variant.Length == 0).ToList();
        foreach (var test in bases.Select(r => r.Test).Distinct(StringComparer.Ordinal))
        {
            var counted = bases.Where(r => r.Test == test && r.Streams is not null).ToList();
            if (counted.Count == 0)
            {
                continue;
            }

            var best = counted.OrderByDescending(r => r.Streams).ThenByDescending(r => r.Fps ?? 0).First();
            text.Append(CultureInfo.InvariantCulture, $"{test}: {(best.Streams == 0 ? "no backend keeps one stream at real time" : $"most streams with {Name(best.Type)} ({Streams(best)})")}\n");
        }

        double? baseFps = null;
        List<string[]> rows = [_headers];
        foreach (var result in report.Results)
        {
            baseFps = result.Variant.Length == 0 ? result.Fps : baseFps;
            rows.Add(Row(result, result.Variant.Length == 0 ? null : baseFps));
        }

        text.Append('\n');
        var widths = Enumerable.Range(0, _headers.Length).Select(i => rows.Max(r => r[i].Length)).ToArray();
        foreach (var row in rows)
        {
            text.Append(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd()).Append('\n');
        }

        var credits = report.Results.Where(r => r.Credit is not null).Select(r => $"{r.Credit} ({r.LicenseUrl})").Distinct(StringComparer.Ordinal).ToList();
        if (credits.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nsamples: {string.Join("; ", credits)}\n");
        }

        return text.ToString();
    }

    /// <summary>Formats one result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="baseFps">The base settings' fps for a comparison row, or null for a base row.</param>
    /// <returns>The cells.</returns>
    private static string[] Row(SpeedResult result, double? baseFps)
    {
        var frameRate = result.FrameRate ?? SpeedCatalog.Find(result.Test)?.FrameRate;
        return
        [
            Name(result.Type),
            result.Device.Length == 0 ? "-" : result.Device,
            result.Test,
            result.Variant.Length == 0 ? "-" : result.Variant,
            Streams(result),
            result.Fps is { } fps && frameRate is { } rate ? (fps / rate).ToString("0.0", CultureInfo.InvariantCulture) + "x" : "-",
            result.Fps is { } f ? f.ToString("0", CultureInfo.InvariantCulture) : "-",
            result.Variant.Length == 0 ? string.Empty : Change(result.Fps, baseFps),
            result.Note ?? string.Empty,
        ];
    }

    /// <summary>Names a backend.</summary>
    /// <param name="type">The backend.</param>
    /// <returns>Its name, or <c>software</c>.</returns>
    private static string Name(HwType type) => type == HwType.none ? "software" : type.ToString();

    /// <summary>Formats a stream count.</summary>
    /// <param name="result">The result.</param>
    /// <returns><c>≈N</c>, <c>N+</c> at the cap, or <c>-</c>.</returns>
    private static string Streams(SpeedResult result) =>
        result.Streams is not { } streams ? "-" : result.Capped ? string.Create(CultureInfo.InvariantCulture, $"{streams}+") : string.Create(CultureInfo.InvariantCulture, $"~{streams}");

    /// <summary>Formats a comparison's change in speed.</summary>
    /// <param name="fps">The comparison's fps.</param>
    /// <param name="baseFps">The base fps.</param>
    /// <returns>A signed percentage, <c>same</c> within run-to-run variation, or <c>-</c>.</returns>
    private static string Change(double? fps, double? baseFps)
    {
        if (fps is not { } value || baseFps is not { } baseline || baseline <= 0)
        {
            return "-";
        }

        var change = (value / baseline) - 1;
        return Math.Abs(change) < Noise ? "same" : change.ToString("+0%;-0%", CultureInfo.InvariantCulture);
    }
}
