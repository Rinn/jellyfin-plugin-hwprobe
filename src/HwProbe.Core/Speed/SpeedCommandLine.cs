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
        return $"{Progress} {Loop(input, startAt)} -t {seconds}{output} -f null -";
    }

    /// <summary>Builds an image run's argument string: the server's own (MediaEncoder.ExtractVideoImagesOnIntervalInternal, v12.2) with the images discarded instead of written.</summary>
    /// <param name="args">Arguments from <see cref="IArgumentSource.BuildImages"/>.</param>
    /// <param name="content">How much of the source to read.</param>
    /// <param name="startAt">Where in the source to start; zero for the beginning.</param>
    /// <param name="loopList">A concat list repeating the source (<see cref="LoopList"/>), read in its place, or null to read the source itself.</param>
    /// <returns>The ffmpeg argument string.</returns>
    /// <remarks>
    /// The input is bounded rather than the output: an image stands for a whole interval, so a bound on the images would stop up to
    /// an interval early or late. A repeat comes from the concat demuxer, not <c>-stream_loop</c>: looping restarts the decoder, which
    /// with NVIDIA's makes ffmpeg rebuild the filter graph, restarting upstream's <c>setpts=N/…</c> count, and MJPEG refuses the
    /// repeated timestamps (jellyfin-ffmpeg 8.1.3, RTX 5080).
    /// </remarks>
    public static string BuildImages(ProbeArguments args, TimeSpan content, TimeSpan startAt = default, string? loopList = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var input = (args.InputArgument ?? throw new ArgumentException("The arguments were generated without the input.", nameof(args))).Trim();
        var threads = args.Threads ?? throw new ArgumentException("The arguments were generated without the thread count.", nameof(args));
        var flags = InputFlags(input);
        if (flags.Count == 0)
        {
            throw new ArgumentException("The input arguments have no -i.", nameof(args));
        }

        var at = flags[0];
        if (loopList is not null)
        {
            var end = ArgumentEnd(input, at + 3);
            input = string.Concat(input.AsSpan(0, at), $"-f concat -safe 0 -i file:\"{loopList.Replace("\"", "\\\"", StringComparison.Ordinal)}\"", input.AsSpan(end));
        }

        var seek = startAt > TimeSpan.Zero ? string.Create(CultureInfo.InvariantCulture, $"-ss {startAt.TotalSeconds:0.###} ") : string.Empty;
        input = input.Insert(at, $"{seek}-t {Seconds(content)} ");
        return string.Create(CultureInfo.InvariantCulture, $"{Progress} {input} -an -sn {args.FilterArgs} -threads {threads} -c:v {args.VideoEncoder} {args.EncoderArgs} -f null -");
    }

    /// <summary>Returns a concat demuxer list that reads a file over and over.</summary>
    /// <param name="path">The file.</param>
    /// <param name="copies">How many times.</param>
    /// <param name="seconds">How long the file plays, or null to take its header's word, which for a downloaded piece is the whole film's length.</param>
    /// <returns>The list.</returns>
    public static string LoopList(string path, int copies, double? seconds)
    {
        ArgumentNullException.ThrowIfNull(path);
        var duration = seconds is { } length ? string.Create(CultureInfo.InvariantCulture, $"duration {length:0.###}\n") : string.Empty;
        return string.Concat(Enumerable.Repeat($"file '{path.Replace("'", "'\\''", StringComparison.Ordinal)}'\n{duration}", copies));
    }

    /// <summary>Builds an audio run's argument string: the server's own with the output discarded, or the input alone, decoded, for a decode test.</summary>
    /// <param name="args">Arguments from <see cref="IArgumentSource.BuildAudio"/>.</param>
    /// <param name="content">How much of the looped source to process.</param>
    /// <returns>The ffmpeg argument string.</returns>
    public static string BuildAudio(AudioArguments args, TimeSpan content)
    {
        ArgumentNullException.ThrowIfNull(args);
        var output = args.Output.Length > 0 ? " " + args.Output : " -vn";
        return $"{Progress} {Loop(args.Input, TimeSpan.Zero)} -t {Seconds(content)}{output} -f null -";
    }

    /// <summary>Loops every input, so the clip and an external subtitle both run as long as asked, and seeks the first (the video) only, as an external subtitle costs the same to draw from its start.</summary>
    /// <param name="input">The input arguments.</param>
    /// <param name="startAt">Where in the first input to start.</param>
    /// <returns>The input arguments with the options added before each <c>-i</c>.</returns>
    private static string Loop(string input, TimeSpan startAt)
    {
        var looped = input.Trim();
        var flags = InputFlags(looped);
        for (var i = flags.Count - 1; i >= 0; i--)
        {
            var seek = i == 0 && startAt > TimeSpan.Zero ? string.Create(CultureInfo.InvariantCulture, $"-ss {startAt.TotalSeconds:0.###} ") : string.Empty;
            looped = looped.Insert(flags[i], seek + "-stream_loop -1 ");
        }

        return looped;
    }

    /// <summary>Finds where an argument ends: at the first space outside quotes, with quotes escaped inside them as upstream escapes them.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="start">Where the argument starts.</param>
    /// <returns>The index after it.</returns>
    private static int ArgumentEnd(string arguments, int start)
    {
        var quoted = false;
        for (var i = start; i < arguments.Length; i++)
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
            else if (!quoted && char.IsWhiteSpace(c))
            {
                return i;
            }
        }

        return arguments.Length;
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
