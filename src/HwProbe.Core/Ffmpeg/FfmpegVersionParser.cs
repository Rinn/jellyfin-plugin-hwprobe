using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Parses <c>ffmpeg -version</c> output the way upstream <c>EncoderValidator</c> (v12.1) does.</summary>
public static partial class FfmpegVersionParser
{
    // Library versions matching ffmpeg 4.4; EncoderValidator._ffmpegMinimumLibraryVersions.
    private static readonly Dictionary<string, Version> _minimumLibraryVersions = new(StringComparer.Ordinal)
    {
        ["libavutil"] = new Version(56, 70),
        ["libavcodec"] = new Version(58, 134),
        ["libavformat"] = new Version(58, 76),
        ["libavdevice"] = new Version(58, 13),
        ["libavfilter"] = new Version(7, 110),
        ["libswscale"] = new Version(5, 9),
        ["libswresample"] = new Version(3, 9),
    };

    /// <summary>Gets the minimum supported ffmpeg version; <c>EncoderValidator.MinVersion</c>.</summary>
    public static Version MinVersion { get; } = new(4, 4);

    /// <summary>Reports whether the output is from Libav's avconv rather than ffmpeg.</summary>
    /// <param name="versionOutput">The <c>-version</c> output.</param>
    /// <returns>True for a Libav build.</returns>
    public static bool IsLibav(string versionOutput)
    {
        ArgumentNullException.ThrowIfNull(versionOutput);
        return versionOutput.Contains("Libav developers", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reports whether the build is jellyfin-ffmpeg, which sets <c>--extra-version=Jellyfin</c>.</summary>
    /// <param name="versionOutput">The <c>-version</c> output.</param>
    /// <returns>True for a jellyfin-ffmpeg build.</returns>
    public static bool IsJellyfinBuild(string versionOutput)
    {
        ArgumentNullException.ThrowIfNull(versionOutput);
        var firstLine = versionOutput.AsSpan().EnumerateLines().GetEnumerator();
        var head = firstLine.MoveNext() ? firstLine.Current : [];
        return head.Contains("-Jellyfin", StringComparison.Ordinal)
            || versionOutput.Contains("--extra-version=Jellyfin", StringComparison.Ordinal);
    }

    /// <summary>Parses the ffmpeg version; <c>EncoderValidator.GetFFmpegVersionInternal</c>.</summary>
    /// <param name="versionOutput">The <c>-version</c> output.</param>
    /// <returns>
    /// The banner version; else <see cref="MinVersion"/> if every library meets its 4.4 minimum;
    /// else null.
    /// </returns>
    public static Version? Parse(string versionOutput)
    {
        ArgumentNullException.ThrowIfNull(versionOutput);

        var match = FfmpegVersionRegex().Match(versionOutput);
        if (match.Success && Version.TryParse(match.Groups[1].ValueSpan, out var version))
        {
            return version;
        }

        var libraries = new Dictionary<string, Version>(StringComparer.Ordinal);
        foreach (Match library in LibraryRegex().Matches(versionOutput))
        {
            libraries[library.Groups["name"].Value] = new Version(
                int.Parse(library.Groups["major"].ValueSpan, CultureInfo.InvariantCulture),
                int.Parse(library.Groups["minor"].ValueSpan, CultureInfo.InvariantCulture));
        }

        var allMeetMinimum = _minimumLibraryVersions.All(
            minimum => libraries.TryGetValue(minimum.Key, out var found) && found >= minimum.Value);
        return allMeetMinimum ? MinVersion : null;
    }

    /// <summary>Validates the build the way upstream does before trusting it.</summary>
    /// <param name="versionOutput">The <c>-version</c> output.</param>
    /// <returns>The validation outcome.</returns>
    public static FfmpegValidation Validate(string versionOutput)
    {
        ArgumentNullException.ThrowIfNull(versionOutput);

        if (string.IsNullOrWhiteSpace(versionOutput))
        {
            return FfmpegValidation.NoOutput;
        }

        if (IsLibav(versionOutput))
        {
            return FfmpegValidation.Libav;
        }

        var version = Parse(versionOutput);
        if (version is null)
        {
            return FfmpegValidation.UnknownVersion;
        }

        return version < MinVersion ? FfmpegValidation.TooOld : FfmpegValidation.Valid;
    }

    /// <summary>Matches the banner version; <c>EncoderValidator.FfmpegVersionRegex</c>.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"^ffmpeg version n?((?:[0-9]+\.?)+)")]
    private static partial Regex FfmpegVersionRegex();

    /// <summary>Matches library versions; <c>EncoderValidator.LibraryRegex</c>.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"((?<name>lib\w+)\s+(?<major>[0-9]+)\.\s*(?<minor>[0-9]+))", RegexOptions.Multiline)]
    private static partial Regex LibraryRegex();
}
