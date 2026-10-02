using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.HwProbe.Core.Diagnostics;

/// <summary>Replaces paths and names that identify a user or host before a diagnostics bundle is written.</summary>
public sealed class DiagnosticsScrubber
{
    private readonly List<(string Value, string Replacement)> _paths;
    private readonly List<(Regex Pattern, string Replacement)> _names;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticsScrubber"/> class.</summary>
    /// <param name="paths">Directories and their placeholders, e.g. the cache root as <c>&lt;cache&gt;</c>; the longest is replaced first.</param>
    /// <param name="names">Words and their placeholders, e.g. the user name as <c>&lt;user&gt;</c>; matched as whole words.</param>
    public DiagnosticsScrubber(IEnumerable<(string Value, string Replacement)> paths, IEnumerable<(string Value, string Replacement)> names)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(names);

        // Trailing separators are trimmed so "/home/a/" and "/home/a" match the same text.
        _paths = [.. paths
            .Select(p => (Value: p.Value.TrimEnd('/', '\\'), p.Replacement))
            .Where(p => p.Value.Length > 1)
            .OrderByDescending(p => p.Value.Length)];

        // Shorter names would replace parts of ordinary words and ffmpeg options.
        _names = [.. names
            .Where(n => n.Value.Length >= 3)
            .Select(n => (new Regex($"(?<![A-Za-z0-9]){Regex.Escape(n.Value)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), n.Replacement))];
    }

    /// <summary>Creates a scrubber for this process: the home directory, user name and host name, plus the given directories.</summary>
    /// <param name="paths">Further directories and their placeholders.</param>
    /// <returns>The scrubber.</returns>
    public static DiagnosticsScrubber ForCurrentHost(params (string Value, string Replacement)[] paths)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new DiagnosticsScrubber(
            [.. paths, (home, "~")],
            [(Environment.UserName, "<user>"), (Environment.MachineName, "<host>")]);
    }

    /// <summary>Returns the text with every path and name replaced.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The scrubbed text.</returns>
    public string Scrub(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var (value, replacement) in _paths)
        {
            text = text.Replace(value, replacement, StringComparison.OrdinalIgnoreCase);

            // JSON escapes Windows path separators.
            if (value.Contains('\\', StringComparison.Ordinal))
            {
                text = text.Replace(value.Replace("\\", "\\\\", StringComparison.Ordinal), replacement, StringComparison.OrdinalIgnoreCase);
            }
        }

        foreach (var (pattern, replacement) in _names)
        {
            text = pattern.Replace(text, replacement);
        }

        return text;
    }
}
