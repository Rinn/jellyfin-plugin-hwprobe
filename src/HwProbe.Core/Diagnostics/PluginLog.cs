using System.Globalization;
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

        // The server's own logs, not the per-transcode FFmpeg.*.log files beside them.
        foreach (var file in Directory.EnumerateFiles(directory, "log_*.log").Order(StringComparer.Ordinal))
        {
            try
            {
                // Jellyfin keeps today's file open for writing.
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var keep = false;
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    keep = Keeps(line) ?? keep;
                    if (keep)
                    {
                        text.Append(line).Append('\n');
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Jellyfin's log cleanup can delete a file after it's listed; the zip goes without it.
                text.Append(CultureInfo.InvariantCulture, $"# {Path.GetFileName(file)} could not be read: {e.Message}\n");
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
            keep = Keeps(line) ?? keep;
            if (keep)
            {
                yield return line;
            }
        }
    }

    /// <summary>Decides whether a line that starts an entry is kept.</summary>
    /// <param name="line">A log line.</param>
    /// <returns>Whether the entry is HwProbe's or names it; null for a line that doesn't start an entry, such as a stack trace, which goes with the entry before it.</returns>
    private static bool? Keeps(string line) => Entry().IsMatch(line) ? line.Contains(Name, StringComparison.Ordinal) : null;

    /// <summary>Matches the start of a Jellyfin log entry: <c>[time] [LVL] [thread] Category: message</c>.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^\[[^\]]+\] \[[A-Z]{3}\] \[[^\]]*\] [^:\s]+: ")]
    private static partial Regex Entry();
}
