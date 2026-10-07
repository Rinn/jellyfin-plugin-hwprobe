using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Probes;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Assembles a speed run's command line around upstream's input, filter, encoder and audio arguments.</summary>
/// <remarks>The wrapper (looping, duration, progress, null output) is synthesized, as the probe's is; <c>-threads</c> sits before the filters, as in DynamicHlsController's command line (v12.2).</remarks>
public static class SpeedCommandLine
{
    // Every run's start: quiet but for warnings, with progress on stdout.
    private const string Progress = "-hide_banner -v warning -nostats -progress pipe:1";

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
        var seconds = Seconds(content);
        var output = decodeOnly
            ? " -an"
            : $"{(args.Threads is { } threads ? string.Create(CultureInfo.InvariantCulture, $" -threads {threads}") : string.Empty)}{args.FilterArgs} -c:v {args.VideoEncoder}{args.EncoderArgs}{(args.AudioArgs.Length > 0 ? args.AudioArgs : " -an")}";

        // No blanket space clean-up: a library path may hold two spaces in a row.
        return $"{Progress} {Loop(input, startAt, null)} -t {seconds}{output} -f null -";
    }

    /// <summary>Builds an image run's argument string: the server's own (MediaEncoder.ExtractVideoImagesOnIntervalInternal, v12.2) with the images discarded instead of written.</summary>
    /// <param name="args">Arguments from <see cref="IArgumentSource.BuildImages"/>.</param>
    /// <param name="content">How much of the looped source to read.</param>
    /// <param name="startAt">Where in the source to start; zero for the beginning.</param>
    /// <returns>The ffmpeg argument string.</returns>
    /// <remarks>The input is bounded rather than the output: an image stands for a whole interval, so a bound on the images would stop up to an interval early or late.</remarks>
    public static string BuildImages(ProbeArguments args, TimeSpan content, TimeSpan startAt = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        var input = args.InputArgument ?? throw new ArgumentException("The arguments were generated without the input.", nameof(args));
        var threads = args.Threads ?? throw new ArgumentException("The arguments were generated without the thread count.", nameof(args));
        return string.Create(CultureInfo.InvariantCulture, $"{Progress} {Loop(input, startAt, content)} -an -sn {args.FilterArgs} -threads {threads} -c:v {args.VideoEncoder} {args.EncoderArgs} -f null -");
    }

    /// <summary>Loops every input, so the clip and an external subtitle both run as long as asked, and seeks and bounds the first (the video) only, as an external subtitle costs the same to draw from its start.</summary>
    /// <param name="input">The input arguments.</param>
    /// <param name="startAt">Where in the first input to start.</param>
    /// <param name="duration">How much of the first input to read, or null to bound the output instead.</param>
    /// <returns>The input arguments with the options added before each <c>-i</c>.</returns>
    private static string Loop(string input, TimeSpan startAt, TimeSpan? duration)
    {
        var looped = input.Trim();
        var flags = InputFlags(looped);
        for (var i = flags.Count - 1; i >= 0; i--)
        {
            var seek = i == 0 && startAt > TimeSpan.Zero ? string.Create(CultureInfo.InvariantCulture, $"-ss {startAt.TotalSeconds:0.###} ") : string.Empty;
            var bound = i == 0 && duration is { } length ? $"-t {Seconds(length)} " : string.Empty;
            looped = looped.Insert(flags[i], seek + bound + "-stream_loop -1 ");
        }

        return looped;
    }

    /// <summary>Formats a duration as ffmpeg takes it.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>Seconds, with up to three decimals.</returns>
    private static string Seconds(TimeSpan duration) => duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

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
