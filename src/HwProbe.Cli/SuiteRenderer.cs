using System.Globalization;
using System.Text;
using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Renders a test suite's runs as one table: a row per backend, input, and output, and a column per step.</summary>
internal static class SuiteRenderer
{
    /// <summary>Renders the comparison.</summary>
    /// <param name="name">The suite's name.</param>
    /// <param name="reports">One report per step, in order, each stamped with its step.</param>
    /// <returns>The text, newline-terminated.</returns>
    public static string Render(string name, IReadOnlyList<SpeedReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        static string Key(SpeedResult r) => $"{r.Type}|{r.Device}|{r.Test}|{r.Variant}";
        var rows = reports.SelectMany(r => r.Results).Where(r => !r.Pending).DistinctBy(Key).ToList();
        List<string[]> table = [["TYPE", "TEST", .. reports.Select(r => r.SuiteStep ?? string.Empty)]];
        foreach (var row in rows)
        {
            List<string> cells = [SpeedRenderer.Name(row.Type), row.Label ?? row.Test];
            foreach (var report in reports)
            {
                var found = report.Results.FirstOrDefault(r => Key(r) == Key(row));
                var rate = found?.FrameRate ?? SpeedCatalog.Find(row.Test)?.FrameRate;
                cells.Add(found?.Fps is { } fps && rate is { } r ? (fps / r).ToString("0.0", CultureInfo.InvariantCulture) + "x" + (found.Streams is null ? string.Empty : " " + SpeedRenderer.Streams(found)) : found is null ? string.Empty : "-");
            }

            table.Add([.. cells]);
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"\nsuite   {name}: speed as a multiple of real time for each step, then streams kept at real time when counted.\n\n");
        var widths = Enumerable.Range(0, table[0].Length).Select(i => table.Max(r => r[i].Length)).ToArray();
        foreach (var row in table)
        {
            text.Append(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd()).Append('\n');
        }

        return text.ToString();
    }
}
