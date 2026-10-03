using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Probes;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Assembles a speed run's command line around upstream's input, filter, encoder and audio arguments.</summary>
/// <remarks>The wrapper (looping, duration, progress, null output) is synthesized, as the probe's is; <c>-threads</c> sits before the filters, as in DynamicHlsController's command line (v12.1).</remarks>
public static class SpeedCommandLine
{
    /// <summary>Builds the argument string.</summary>
    /// <param name="args">Arguments generated with <see cref="ProbeCell.FullQuality"/>.</param>
    /// <param name="content">How much of the looped source to process.</param>
    /// <param name="decodeOnly">Whether to stop after decoding, with no filters, encode or audio.</param>
    /// <param name="startAt">Where in the source to start; zero for the beginning.</param>
    /// <returns>The ffmpeg argument string.</returns>
    public static string Build(ProbeArguments args, TimeSpan content, bool decodeOnly, TimeSpan startAt = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        var input = args.InputArgument ?? throw new ArgumentException("The arguments were generated without the input.", nameof(args));

        // Loop every input, so the clip and an external subtitle both run as long as asked; seek the first (the video) only, as an external subtitle costs the same to draw from its start.
        var looped = input.Trim();
        var flags = InputFlags(looped);
        for (var i = flags.Count - 1; i >= 0; i--)
        {
            var seek = i == 0 && startAt > TimeSpan.Zero ? string.Create(CultureInfo.InvariantCulture, $"-ss {startAt.TotalSeconds:0.###} ") : string.Empty;
            looped = looped.Insert(flags[i], seek + "-stream_loop -1 ");
        }

        var seconds = content.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var output = decodeOnly
            ? " -an"
            : $"{(args.Threads is { } threads ? string.Create(CultureInfo.InvariantCulture, $" -threads {threads}") : string.Empty)}{args.FilterArgs} -c:v {args.VideoEncoder}{args.EncoderArgs}{(args.AudioArgs.Length > 0 ? args.AudioArgs : " -an")}";

        // No blanket space clean-up: a library path may hold two spaces in a row.
        return $"-hide_banner -v warning -nostats -progress pipe:1 {looped} -t {seconds}{output} -f null -";
    }

    /// <summary>Finds each <c>-i</c> option outside quotes, so a path holding <c> -i </c> isn't taken for one.</summary>
    /// <param name="arguments">The input arguments, with paths quoted and their quotes escaped as upstream does.</param>
    /// <returns>Where each option starts.</returns>
    private static List<int> InputFlags(string arguments)
    {
        List<int> found = [];
        var quoted = false;
        for (var i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];
            if (c == '\\' && quoted)
            {
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c == '-' && (i == 0 || char.IsWhiteSpace(arguments[i - 1])) && i + 2 < arguments.Length && arguments[i + 1] == 'i' && char.IsWhiteSpace(arguments[i + 2]))
            {
                found.Add(i);
            }
        }

        return found;
    }
}
