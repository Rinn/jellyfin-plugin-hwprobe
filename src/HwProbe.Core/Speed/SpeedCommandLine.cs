using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HwProbe.Core.Probes;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Assembles a speed run's command line around upstream's input, filter, encoder and audio arguments.</summary>
/// <remarks>The wrapper (looping, duration, progress, null output) is synthesized, as the probe's is.</remarks>
public static partial class SpeedCommandLine
{
    /// <summary>Builds the argument string.</summary>
    /// <param name="args">Arguments generated with <see cref="ProbeCell.FullQuality"/>.</param>
    /// <param name="content">How much of the looped source to process.</param>
    /// <param name="decodeOnly">Whether to stop after decoding, with no filters, encode or audio.</param>
    /// <returns>The ffmpeg argument string.</returns>
    public static string Build(ProbeArguments args, TimeSpan content, bool decodeOnly)
    {
        ArgumentNullException.ThrowIfNull(args);
        var input = args.InputArgument ?? throw new ArgumentException("The arguments were generated without the input.", nameof(args));

        // Loop every input, so the clip and an external subtitle both run as long as asked.
        var looped = InputFlag().Replace(input.Trim(), "${space}-stream_loop -1 -i ");
        var seconds = content.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var output = decodeOnly
            ? " -an"
            : $"{args.FilterArgs} -c:v {args.VideoEncoder}{args.EncoderArgs}{(args.AudioArgs.Length > 0 ? args.AudioArgs : " -an")}";
        return $"-hide_banner -v warning -nostats -progress pipe:1 {looped} -t {seconds}{output} -f null -".Replace("  ", " ", StringComparison.Ordinal);
    }

    /// <summary>Matches each <c>-i</c> option.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"(?<space>^|\s)-i\s")]
    private static partial Regex InputFlag();
}
