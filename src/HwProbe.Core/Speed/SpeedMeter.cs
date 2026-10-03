using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Finds one command's fps and how many copies of it keep real time at once.</summary>
/// <remarks>The launches are passed in, so the counting is tested without ffmpeg.</remarks>
public static class SpeedMeter
{
    /// <summary>The most copies run at once; more is reported as at least this many.</summary>
    public const int MaxStreams = 16;

    /// <summary>The content each copy transcodes: long enough that start-up is a small part of it.</summary>
    public static readonly TimeSpan Content = TimeSpan.FromSeconds(10);

    // ffmpeg start-up (device init, probing the input) on top of real time.
    private static readonly TimeSpan _startup = TimeSpan.FromSeconds(1);

    // A single run shorter than this is repeated with more content, so start-up doesn't skew its fps.
    private static readonly TimeSpan _shortest = TimeSpan.FromSeconds(5);

    // Content cap for the repeated single run, for hosts that run hundreds of times real time.
    private static readonly TimeSpan _longestContent = TimeSpan.FromMinutes(10);

    /// <summary>Measures one command.</summary>
    /// <param name="launch">Starts this many copies together, each transcoding this much content, and returns when all have ended.</param>
    /// <param name="method">How streams are counted.</param>
    /// <param name="frameRate">The source frame rate.</param>
    /// <param name="countStreams">False for a decode test, which reports fps only.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <param name="timeUp">Reports when the measurement's time limit has passed; checked before each run of copies, never before the first run. Null for no limit.</param>
    /// <returns>The fps and stream count.</returns>
    public static async Task<SpeedMeasurement> MeasureAsync(
        Func<int, TimeSpan, CancellationToken, Task<IReadOnlyList<FfmpegRunResult>>> launch,
        SpeedMethod method,
        double frameRate,
        bool countStreams,
        CancellationToken cancellationToken,
        Func<bool>? timeUp = null)
    {
        ArgumentNullException.ThrowIfNull(launch);

        var single = (await launch(1, Content, cancellationToken))[0];
        var longerCutOff = false;
        var fps = Fps(single);
        if (single.Status == FfmpegRunStatus.Exited && single.ExitCode == 0 && single.Duration < _shortest && single.Duration > TimeSpan.Zero)
        {
            // Enough content to take about as long as the content plays. Start-up, and restarting a short looped
            // clip (slow with NVIDIA's cuvid decoders), only slow a run down, so the faster run is the closer figure.
            var longer = TimeSpan.FromSeconds(Math.Min(Content.TotalSeconds * Content.TotalSeconds / single.Duration.TotalSeconds, _longestContent.TotalSeconds));
            var longerRun = (await launch(1, longer, cancellationToken))[0];
            var longerFps = Fps(longerRun);
            fps = fps is { } first && longerFps is { } second ? Math.Max(first, second) : fps ?? longerFps;
            longerCutOff = longerRun.Status == FfmpegRunStatus.TimedOut;
        }

        if (fps is null)
        {
            return new SpeedMeasurement(null, null, false, Failure(single)) { Interrupted = single.Status == FfmpegRunStatus.TimedOut };
        }

        // A single copy killed by its timeout gives fps from the frames it reached.
        var cutOff = single.Status == FfmpegRunStatus.TimedOut || longerCutOff;
        if (!countStreams)
        {
            return new SpeedMeasurement(fps, null, false, null) { Interrupted = cutOff };
        }

        // One copy's speed doesn't say how many keep up together: copies share the CPU, and a GPU often runs several sessions faster in total than one.
        if (method == SpeedMethod.Quick)
        {
            return new SpeedMeasurement(fps, null, false, null) { Interrupted = cutOff };
        }

        var start = Math.Clamp((int)Math.Floor(fps.Value / frameRate), 1, MaxStreams);

        var erroredAt = 0;
        var keptUp = 0;
        async Task<bool> KeepsUpAsync(int copies)
        {
            if (timeUp?.Invoke() == true)
            {
                throw new TimeoutException();
            }

            var runs = await launch(copies, Content, cancellationToken);
            if (runs.Any(r => r.Status == FfmpegRunStatus.LaunchFailed || (r.Status == FfmpegRunStatus.Exited && r.ExitCode != 0)))
            {
                erroredAt = erroredAt == 0 ? copies : Math.Min(erroredAt, copies);
                return false;
            }

            var kept = runs.All(r => r.Status == FfmpegRunStatus.Exited && r.Duration <= Content + _startup);
            keptUp = kept ? Math.Max(keptUp, copies) : keptUp;
            return kept;
        }

        int streams;
        try
        {
            streams = await CountAsync(KeepsUpAsync, method, start);
        }
        catch (TimeoutException)
        {
            return keptUp > 0
                ? new SpeedMeasurement(fps, keptUp, false, string.Create(CultureInfo.InvariantCulture, $"Time limit reached: at least {keptUp}.")) { Interrupted = true }
                : new SpeedMeasurement(fps, null, false, "Time limit reached before streams were counted.") { Interrupted = true };
        }

        // Copies that fail rather than fall behind usually hit the driver's limit on sessions at once (NVENC has one).
        var note = erroredAt == streams + 1
            ? string.Create(CultureInfo.InvariantCulture, $"{erroredAt} at once failed to start, likely the driver's limit on sessions rather than speed.")
            : null;
        return new SpeedMeasurement(fps, streams, streams == MaxStreams, note) { Interrupted = cutOff };
    }

    /// <summary>Counts the streams that keep up: doubling from a starting count until they fall behind, then narrowing down.</summary>
    /// <param name="keepsUp">Runs that many copies and reports whether all kept real time.</param>
    /// <param name="method">Confirm starts from one copy's speed; full starts from one copy.</param>
    /// <param name="start">One copy's speed as a whole number, at least 1.</param>
    /// <returns>The streams.</returns>
    private static async Task<int> CountAsync(Func<int, Task<bool>> keepsUp, SpeedMethod method, int start)
    {
        var first = method == SpeedMethod.Confirm ? start : 1;
        if (!await keepsUp(first))
        {
            return await SearchAsync(keepsUp, 1, first - 1);
        }

        var passed = first;
        var failed = 0;
        for (var copies = Math.Min(first * 2, MaxStreams); passed < MaxStreams; copies = Math.Min(copies * 2, MaxStreams))
        {
            if (!await keepsUp(copies))
            {
                failed = copies;
                break;
            }

            passed = copies;
        }

        return failed == 0 ? passed : await SearchAsync(keepsUp, passed + 1, failed - 1, passed);
    }

    /// <summary>Finds the most copies in a range that keep up, assuming fewer keep up when more do.</summary>
    /// <param name="keepsUp">Runs that many copies and reports whether all kept real time.</param>
    /// <param name="low">The fewest to try.</param>
    /// <param name="high">The most to try.</param>
    /// <param name="known">Copies already known to keep up.</param>
    /// <returns>The most that kept up, or <paramref name="known"/> when none in the range did.</returns>
    private static async Task<int> SearchAsync(Func<int, Task<bool>> keepsUp, int low, int high, int known = 0)
    {
        while (low <= high)
        {
            var middle = (low + high + 1) / 2;
            if (await keepsUp(middle))
            {
                known = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return known;
    }

    /// <summary>Frames over wall time, including a run cut off by its timeout.</summary>
    /// <param name="result">The run.</param>
    /// <returns>The fps, or null when the run failed or produced no frames.</returns>
    private static double? Fps(FfmpegRunResult result) =>
        result.Frames is > 0 and var frames && result.Duration > TimeSpan.Zero && (result.Status == FfmpegRunStatus.TimedOut || result.ExitCode == 0)
            ? frames / result.Duration.TotalSeconds
            : null;

    /// <summary>Describes a run that produced no fps.</summary>
    /// <param name="result">The run.</param>
    /// <returns>The reason, with ffmpeg's last stderr line when there is one.</returns>
    private static string Failure(FfmpegRunResult result)
    {
        var last = result.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        var ended = result.Status switch
        {
            FfmpegRunStatus.LaunchFailed => $"ffmpeg didn't start: {result.LaunchError}",
            FfmpegRunStatus.TimedOut => "ffmpeg produced no frames before the time limit",
            _ => string.Create(CultureInfo.InvariantCulture, $"ffmpeg exited with {result.ExitCode}"),
        };
        return last is null ? ended : $"{ended}: {last}";
    }
}
