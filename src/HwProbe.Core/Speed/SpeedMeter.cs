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
    /// <returns>The fps and stream count.</returns>
    public static async Task<SpeedMeasurement> MeasureAsync(
        Func<int, TimeSpan, CancellationToken, Task<IReadOnlyList<FfmpegRunResult>>> launch,
        SpeedMethod method,
        double frameRate,
        bool countStreams,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);

        var single = (await launch(1, Content, cancellationToken))[0];
        var fps = Fps(single);
        if (single.Status == FfmpegRunStatus.Exited && single.ExitCode == 0 && single.Duration < _shortest && single.Duration > TimeSpan.Zero)
        {
            // Enough content to take about as long as the content plays. Start-up, and restarting a short looped
            // clip (slow with NVIDIA's cuvid decoders), only slow a run down, so the faster run is the closer figure.
            var longer = TimeSpan.FromSeconds(Math.Min(Content.TotalSeconds * Content.TotalSeconds / single.Duration.TotalSeconds, _longestContent.TotalSeconds));
            var longerFps = Fps((await launch(1, longer, cancellationToken))[0]);
            fps = fps is { } first && longerFps is { } second ? Math.Max(first, second) : fps ?? longerFps;
        }

        if (fps is null)
        {
            return new SpeedMeasurement(null, null, false, Failure(single));
        }

        if (!countStreams)
        {
            return new SpeedMeasurement(fps, null, false, null);
        }

        var estimate = Math.Min((int)Math.Floor(fps.Value / frameRate), MaxStreams);
        if (method == SpeedMethod.Quick)
        {
            return new SpeedMeasurement(fps, estimate, estimate == MaxStreams, null);
        }

        var erroredAt = 0;
        async Task<bool> KeepsUpAsync(int copies)
        {
            var runs = await launch(copies, Content, cancellationToken);
            if (runs.Any(r => r.Status == FfmpegRunStatus.LaunchFailed || (r.Status == FfmpegRunStatus.Exited && r.ExitCode != 0)))
            {
                erroredAt = erroredAt == 0 ? copies : Math.Min(erroredAt, copies);
                return false;
            }

            return runs.All(r => r.Status == FfmpegRunStatus.Exited && r.Duration <= Content + _startup);
        }

        int streams;
        if (method == SpeedMethod.Confirm)
        {
            streams = estimate > 0 && await KeepsUpAsync(estimate) ? estimate : await SearchAsync(KeepsUpAsync, 1, estimate - 1);
        }
        else
        {
            var passed = 0;
            var failed = 0;
            for (var copies = 1; copies <= MaxStreams; copies *= 2)
            {
                if (!await KeepsUpAsync(copies))
                {
                    failed = copies;
                    break;
                }

                passed = copies;
            }

            streams = failed == 0 ? passed : await SearchAsync(KeepsUpAsync, passed + 1, failed - 1, passed);
        }

        // Copies that fail rather than fall behind usually hit the driver's limit on sessions at once (NVENC has one).
        var note = erroredAt == streams + 1
            ? string.Create(CultureInfo.InvariantCulture, $"{erroredAt} at once failed to start, likely the driver's limit on sessions rather than speed.")
            : null;
        return new SpeedMeasurement(fps, streams, streams == MaxStreams, note);
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
