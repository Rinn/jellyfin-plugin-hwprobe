using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Reads ffmpeg's <c>-progress pipe:1</c> key=value output.</summary>
public static class ProgressParser
{
    /// <summary>Returns the last reported frame count.</summary>
    /// <param name="progress">Captured stdout containing progress blocks.</param>
    /// <returns>The last <c>frame=</c> value, or null when none parses.</returns>
    public static long? LastFrame(string progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        long? last = null;
        foreach (var line in progress.AsSpan().EnumerateLines())
        {
            if (line.StartsWith("frame=", StringComparison.Ordinal)
                && long.TryParse(line["frame=".Length..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var frame))
            {
                last = frame;
            }
        }

        return last;
    }
}
