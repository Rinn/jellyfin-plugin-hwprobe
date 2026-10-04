using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.HwProbe.Core.Diagnostics;

/// <summary>Picks HwProbe's entries out of Jellyfin's log files, for the diagnostics zip.</summary>
public static partial class PluginLog
{
    private const string Name = "HwProbe";

    /// <summary>Reads every log file in a folder, oldest first, and keeps HwProbe's entries.</summary>
    /// <param name="directory">Jellyfin's log folder, or null.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The entries, each with its continuation lines; empty when there's no folder.</returns>
    public static async Task<string> ReadAsync(string? directory, CancellationToken cancellationToken)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(directory, "*.log").Order(StringComparer.Ordinal))
        {
            // Jellyfin keeps today's file open for writing.
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            List<string> lines = [];
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lines.Add(line);
            }

            foreach (var line in Filter(lines))
            {
                text.Append(line).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>Keeps entries logged by HwProbe or naming it, such as the plugin manager loading it, with their continuation lines.</summary>
    /// <param name="lines">Log lines in order.</param>
    /// <returns>The kept lines.</returns>
    public static IEnumerable<string> Filter(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var keep = false;
        foreach (var line in lines)
        {
            // A line that doesn't start an entry, such as a stack trace, belongs to the one before it.
            if (Entry().Match(line) is { Success: true } entry)
            {
                keep = entry.Groups["category"].Value.StartsWith("Jellyfin.Plugin." + Name, StringComparison.Ordinal) || line.Contains(Name, StringComparison.Ordinal);
            }

            if (keep)
            {
                yield return line;
            }
        }
    }

    /// <summary>Matches the start of a Jellyfin log entry: <c>[time] [LVL] [thread] Category: message</c>.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^\[[^\]]+\] \[[A-Z]{3}\] \[[^\]]*\] (?<category>[^:\s]+): ")]
    private static partial Regex Entry();
}
