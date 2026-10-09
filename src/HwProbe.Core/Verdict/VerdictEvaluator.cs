using System.Text;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>Classifies ffmpeg runs into probe outcomes.</summary>
public static class VerdictEvaluator
{
    /// <summary>Default bound for <see cref="StderrTail"/>, in UTF-8 bytes.</summary>
    public const int DefaultTailBytes = 4096;

    /// <summary>Classifies a transcode probe (smoke probe and codec matrix).</summary>
    /// <param name="result">The ffmpeg run, made at <c>-v verbose</c> with <c>-progress pipe:1</c>.</param>
    /// <param name="expectation">Frames and confirmation strings required for a pass.</param>
    /// <returns>
    /// <see cref="ProbeOutcome.Pass"/> only for exit 0, enough frames, a confirmation string present and no failure
    /// marker. A run that would otherwise pass but has no confirmation strings is <see cref="ProbeOutcome.Untested"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The run never launched; that is a caller error, not a probe outcome.</exception>
    public static ProbeOutcome Evaluate(FfmpegRunResult result, ProbeExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(expectation);
        RejectLaunchFailure(result);

        if (result.Status == FfmpegRunStatus.TimedOut)
        {
            return ProbeOutcome.Timeout;
        }

        var stderr = result.Stderr;
        var ranToCompletion = result.ExitCode == 0 && result.Frames >= expectation.ExpectedFrames;
        if (!ranToCompletion)
        {
            return ClassifyFailure(stderr);
        }

        // ffmpeg exits 0 with frames after falling back to software.
        var confirmations = expectation.ConfirmationStrings;
        var confirmed = confirmations.Count == 0 || confirmations.Any(n => stderr.Contains(n, StringComparison.Ordinal));
        if (!confirmed || HasFailureMarker(stderr))
        {
            return ProbeOutcome.SoftwareFallback;
        }

        return expectation.ConfirmationStrings.Count == 0 ? ProbeOutcome.Untested : ProbeOutcome.Pass;
    }

    /// <summary>Reports whether ffmpeg died on a signal rather than exiting itself.</summary>
    /// <param name="result">The ffmpeg run.</param>
    /// <returns>True for a SIGABRT or SIGSEGV with no exit line.</returns>
    /// <remarks>
    /// .NET reports a child a signal killed as 128 plus the signal's number. ffmpeg logs "Exiting with exit code" at verbose
    /// level when it exits itself, so a real exit code of 134 or 139 isn't taken for a crash. Observed with jellyfin-ffmpeg
    /// 8.1.3 in Docker on a host with an Intel and an AMD GPU: glibc's "free(): invalid pointer" abort while OpenCL starts.
    /// </remarks>
    public static bool Crashed(FfmpegRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Status == FfmpegRunStatus.Exited && result.ExitCode is 134 or 139 && !result.Stderr.Contains("Exiting with exit code", StringComparison.Ordinal);
    }

    /// <summary>Classifies a software transcode, which has no hardware to confirm.</summary>
    /// <param name="result">The ffmpeg run.</param>
    /// <param name="expectedFrames">Minimum final <c>frame=</c> count.</param>
    /// <returns><see cref="ProbeOutcome.Pass"/> for exit 0 with enough frames; otherwise the failure class.</returns>
    /// <exception cref="ArgumentException">The run never launched.</exception>
    public static ProbeOutcome EvaluateSoftware(FfmpegRunResult result, long expectedFrames)
    {
        ArgumentNullException.ThrowIfNull(result);
        RejectLaunchFailure(result);
        if (result.Status == FfmpegRunStatus.TimedOut)
        {
            return ProbeOutcome.Timeout;
        }

        return result.ExitCode == 0 && result.Frames >= expectedFrames ? ProbeOutcome.Pass : ClassifyFailure(result.Stderr);
    }

    /// <summary>Classifies a bare device open, judged by stderr alone.</summary>
    /// <param name="result">The run of <c>-v verbose -hide_banner -init_hw_device …</c>.</param>
    /// <param name="driverName">Driver name expected in stderr, as in upstream CheckVaapiDeviceByDriverName.</param>
    /// <returns><see cref="ProbeOutcome.Pass"/> when the driver name appears; otherwise the failure class.</returns>
    /// <remarks>With no input or output ffmpeg always exits non-zero, so the exit code is ignored.</remarks>
    /// <exception cref="ArgumentException">The run never launched.</exception>
    public static ProbeOutcome EvaluateDeviceOpen(FfmpegRunResult result, string driverName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrEmpty(driverName);
        RejectLaunchFailure(result);

        if (result.Status == FfmpegRunStatus.TimedOut)
        {
            return ProbeOutcome.Timeout;
        }

        if (ContainsAny(result.Stderr, StderrMarkers.PermissionDenied))
        {
            return ProbeOutcome.PermissionDenied;
        }

        return result.Stderr.Contains(driverName, StringComparison.Ordinal)
            ? ProbeOutcome.Pass
            : ProbeOutcome.DeviceUnavailable;
    }

    /// <summary>Returns the end of a log, bounded in UTF-8 bytes without splitting a character.</summary>
    /// <param name="stderr">The full log.</param>
    /// <param name="maxBytes">Upper bound on the encoded size of the result.</param>
    /// <returns>The longest suffix that encodes to at most <paramref name="maxBytes"/> bytes.</returns>
    public static string StderrTail(string stderr, int maxBytes = DefaultTailBytes)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var bytes = Encoding.UTF8.GetBytes(stderr);
        if (bytes.Length <= maxBytes)
        {
            return stderr;
        }

        var start = bytes.Length - maxBytes;

        // Skip continuation bytes (10xxxxxx) so the cut lands on a character boundary.
        while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80)
        {
            start++;
        }

        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>Maps a failed run to the most specific outcome its stderr supports.</summary>
    /// <param name="stderr">The run's stderr.</param>
    /// <returns>The failure outcome.</returns>
    private static ProbeOutcome ClassifyFailure(string stderr)
    {
        if (ContainsAny(stderr, StderrMarkers.PermissionDenied))
        {
            return ProbeOutcome.PermissionDenied;
        }

        if (ContainsAny(stderr, StderrMarkers.DeviceUnavailable))
        {
            return ProbeOutcome.DeviceUnavailable;
        }

        // A hwaccel that couldn't start hands software frames to the hardware filters, which then fail too;
        // the codec is the cause.
        if (ContainsAny(stderr, StderrMarkers.HwaccelSetupFailed))
        {
            return ProbeOutcome.CodecUnsupported;
        }

        if (ContainsAny(stderr, StderrMarkers.FilterUnsupported))
        {
            return ProbeOutcome.FilterUnsupported;
        }

        // Codec markers, generic failures and unrecognised failures all land here; the stderr tail
        // carries the detail.
        return ProbeOutcome.CodecUnsupported;
    }

    /// <summary>Reports whether stderr holds any marker that rules out a pass.</summary>
    /// <param name="stderr">The run's stderr.</param>
    /// <returns>True if any failure marker appears.</returns>
    private static bool HasFailureMarker(string stderr)
    {
        stderr = StderrMarkers.Harmless.Aggregate(stderr, (text, line) => text.Replace(line, string.Empty, StringComparison.Ordinal));
        return ContainsAny(stderr, StderrMarkers.PermissionDenied)
            || ContainsAny(stderr, StderrMarkers.DeviceUnavailable)
            || ContainsAny(stderr, StderrMarkers.FilterUnsupported)
            || ContainsAny(stderr, StderrMarkers.CodecUnsupported)
            || ContainsAny(stderr, StderrMarkers.Generic);
    }

    /// <summary>Reports whether the text contains any of the markers.</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="markers">Ordinal substrings.</param>
    /// <returns>True on the first match.</returns>
    private static bool ContainsAny(string text, IReadOnlyList<string> markers) =>
        markers.Any(m => text.Contains(m, StringComparison.Ordinal));

    /// <summary>Throws for a run that never started.</summary>
    /// <param name="result">The run.</param>
    private static void RejectLaunchFailure(FfmpegRunResult result)
    {
        if (result.Status == FfmpegRunStatus.LaunchFailed)
        {
            throw new ArgumentException($"ffmpeg did not launch: {result.LaunchError}", nameof(result));
        }
    }
}
