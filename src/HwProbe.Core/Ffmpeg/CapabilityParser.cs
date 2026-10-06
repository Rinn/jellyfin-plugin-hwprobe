using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Parses the build enumerations with upstream's <c>EncoderValidator</c> (v12.2) rules.</summary>
public static partial class CapabilityParser
{
    /// <summary>Parses <c>-encoders</c> or <c>-decoders</c> output.</summary>
    /// <param name="output">The listing.</param>
    /// <returns>Every codec name in the listing.</returns>
    /// <remarks>Upstream then keeps only its required lists; that filter belongs to the consumer.</remarks>
    public static IReadOnlySet<string> ParseCodecs(string output) => Collect(CodecRegex(), "codec", output);

    /// <summary>Parses <c>-filters</c> output.</summary>
    /// <param name="output">The listing.</param>
    /// <returns>Every filter name in the listing.</returns>
    public static IReadOnlySet<string> ParseFilters(string output) => Collect(FilterRegex(), "filter", output);

    /// <summary>Parses <c>-hwaccels</c> output; <c>EncoderValidator.GetHwaccelTypes</c>.</summary>
    /// <param name="output">The listing.</param>
    /// <returns>The hwaccel names, header dropped.</returns>
    public static IReadOnlySet<string> ParseHwaccels(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Skip(1).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Checks <c>-h filter=</c> help for an option; <c>EncoderValidator.CheckFilterWithOption</c>.</summary>
    /// <param name="help">The help output.</param>
    /// <param name="filter">The filter that was asked about.</param>
    /// <param name="requiredText">Text that proves the option exists.</param>
    /// <returns>True only if the help is for this filter and contains the required text.</returns>
    public static bool HasFilterOption(string help, string filter, string requiredText)
    {
        ArgumentNullException.ThrowIfNull(help);
        return help.Contains("Filter " + filter, StringComparison.Ordinal)
            && help.Contains(requiredText, StringComparison.Ordinal);
    }

    /// <summary>Collects one named group from every match.</summary>
    /// <param name="regex">The listing regex.</param>
    /// <param name="group">The group holding the name.</param>
    /// <param name="output">The listing.</param>
    /// <returns>The distinct names.</returns>
    private static HashSet<string> Collect(Regex regex, string group, string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return regex.Matches(output).Select(m => m.Groups[group].Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Matches a codec row; <c>EncoderValidator.CodecRegex</c>.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"^\s\S{6}\s(?<codec>[\w|-]+)\s+.+$", RegexOptions.Multiline)]
    private static partial Regex CodecRegex();

    /// <summary>Matches a filter row; <c>EncoderValidator.FilterRegex</c>.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"^\s\S{2,3}\s(?<filter>[\w|-]+)\s+.+$", RegexOptions.Multiline)]
    private static partial Regex FilterRegex();
}
