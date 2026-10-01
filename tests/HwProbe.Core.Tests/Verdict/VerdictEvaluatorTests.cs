using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;

/// <summary>The three-condition pass rule, failure classification, device open and stderr tail.</summary>
[Trait("Category", "Unit")]
public sealed class VerdictEvaluatorTests
{
    private const string VtFrameFormat = "pixfmt:videotoolbox_vld";

    private static readonly ProbeExpectation _vt = new(10, [VtFrameFormat]);

    /// <summary>A recorded VideoToolbox hardware transcode passes.</summary>
    [Fact]
    public void RecordedVideoToolboxPassIsPass()
    {
        var stderr = CorpusFile.Load("stderr/videotoolbox-h264-pass.txt");

        Assert.Equal(ProbeOutcome.Pass, VerdictEvaluator.Evaluate(Exited(0, 10, stderr), _vt));
    }

    /// <summary>The headline case: exit 0 and frames, but decoded in software.</summary>
    [Fact]
    public void RecordedCleanExitSoftwareFallbackIsNotPass()
    {
        var stderr = CorpusFile.Load("stderr/videotoolbox-mpeg4-sw-fallback.txt");

        Assert.Equal(ProbeOutcome.SoftwareFallback, VerdictEvaluator.Evaluate(Exited(0, 10, stderr), _vt));
    }

    /// <summary>Confirmation present but a failure marker also present is still a fallback.</summary>
    /// <param name="marker">A marker that rules out a pass.</param>
    [Theory]
    [InlineData("Failed to sync surface")]
    [InlineData("No device available")]
    [InlineData("10-bit not supported")]
    [InlineData("Impossible to convert between the formats")]
    [InlineData("Failed setup for format vaapi: hwaccel initialisation returned error.")]
    public void ConfirmedButMarkedIsFallback(string marker) =>
        Assert.Equal(ProbeOutcome.SoftwareFallback, VerdictEvaluator.Evaluate(Exited(0, 10, $"{VtFrameFormat}\n{marker}\n"), _vt));

    /// <summary>Confirmation strings are alternatives: either confirms, neither is a fallback.</summary>
    /// <param name="stderr">Captured stderr.</param>
    /// <param name="expected">The outcome.</param>
    [Theory]
    [InlineData("Reinit context to 640x368, pix_fmt: vaapi\n", ProbeOutcome.Pass)]
    [InlineData("graph input w:640 pixfmt:vaapi\n", ProbeOutcome.Pass)]
    [InlineData("[hevc @ 0x1] Format vaapi chosen by get_format().\n", ProbeOutcome.Pass)]
    [InlineData("[mpeg2video @ 0x1] Failed setup for format vaapi: hwaccel initialisation returned error.\n[mpeg2video @ 0x1] Format vaapi chosen by get_format().\n", ProbeOutcome.SoftwareFallback)]
    [InlineData("graph input w:640 pixfmt:nv12\n", ProbeOutcome.SoftwareFallback)]
    public void AnyConfirmationStringPasses(string stderr, ProbeOutcome expected) =>
        Assert.Equal(expected, VerdictEvaluator.Evaluate(Exited(0, 10, stderr), new ProbeExpectation(10, StderrMarkers.HardwareFrames(HwType.vaapi))));

    /// <summary>Exit 0 with too few frames is not a pass.</summary>
    /// <param name="frames">The final frame count, or null for none reported.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(9L)]
    [InlineData(null)]
    public void TooFewFramesIsNotPass(long? frames) =>
        Assert.NotEqual(ProbeOutcome.Pass, VerdictEvaluator.Evaluate(Exited(0, frames, VtFrameFormat), _vt));

    /// <summary>No confirmation strings means the cell cannot be confirmed (v4l2m2m).</summary>
    [Fact]
    public void NoConfirmationStringsIsUntested() =>
        Assert.Equal(ProbeOutcome.Untested, VerdictEvaluator.Evaluate(Exited(0, 10, string.Empty), new ProbeExpectation(10, [])));

    /// <summary>No confirmation strings but a failure marker is a fallback, not untested.</summary>
    [Fact]
    public void NoConfirmationStringsWithMarkerIsFallback() =>
        Assert.Equal(ProbeOutcome.SoftwareFallback, VerdictEvaluator.Evaluate(Exited(0, 10, "Failed to open codec\n"), new ProbeExpectation(10, [])));

    /// <summary>Failed runs are classified by the most specific marker.</summary>
    /// <param name="stderr">The run's stderr.</param>
    /// <param name="expected">The expected outcome.</param>
    [Theory]
    [InlineData("Failed to set value 'vaapi=va:/dev/dri/renderD128' for option 'init_hw_device': Permission denied\n", ProbeOutcome.PermissionDenied)]
    [InlineData("Device creation failed: -12.\n", ProbeOutcome.DeviceUnavailable)]
    [InlineData("Failed to open /dev/dri/renderD129 as DRM device node.\n", ProbeOutcome.DeviceUnavailable)]
    [InlineData("Failed to initialise VAAPI connection: -1 (unknown libva error).\n", ProbeOutcome.DeviceUnavailable)]
    [InlineData("Cannot open a VA display from DRM device /dev/dri/renderD128.\n", ProbeOutcome.DeviceUnavailable)]
    [InlineData("No device available for decoder\n", ProbeOutcome.DeviceUnavailable)]
    [InlineData("Impossible to convert between the formats supported by the filter 'a' and the filter 'b'\n", ProbeOutcome.FilterUnsupported)]
    [InlineData("No such filter: 'scale_vaapi'\n", ProbeOutcome.FilterUnsupported)]
    [InlineData("Error reinitializing filters!\n", ProbeOutcome.FilterUnsupported)]
    [InlineData("Error while opening encoder - maybe incorrect parameters\n", ProbeOutcome.CodecUnsupported)]
    [InlineData("Unknown encoder 'av1_vaapi'\n", ProbeOutcome.CodecUnsupported)]
    [InlineData("profile not supported\n", ProbeOutcome.CodecUnsupported)]
    [InlineData("something unrecognised\n", ProbeOutcome.CodecUnsupported)]
    public void FailedRunsAreClassified(string stderr, ProbeOutcome expected) =>
        Assert.Equal(expected, VerdictEvaluator.Evaluate(Exited(1, null, stderr), _vt));

    /// <summary>Permission denied wins over the device-creation line that accompanies it.</summary>
    [Fact]
    public void PermissionDeniedBeatsDeviceCreationFailed() =>
        Assert.Equal(ProbeOutcome.PermissionDenied, VerdictEvaluator.Evaluate(Exited(1, null, "Device creation failed: -13.\nPermission denied\n"), _vt));

    /// <summary>A timeout is reported as such regardless of stderr.</summary>
    [Fact]
    public void TimeoutIsTimeout() =>
        Assert.Equal(ProbeOutcome.Timeout, VerdictEvaluator.Evaluate(Run(FfmpegRunStatus.TimedOut, null, null, VtFrameFormat), _vt));

    /// <summary>A launch failure is a caller error, not an outcome.</summary>
    [Fact]
    public void LaunchFailureThrows()
    {
        var result = new FfmpegRunResult(FfmpegRunStatus.LaunchFailed, null, string.Empty, string.Empty, null, TimeSpan.Zero, "nope");

        Assert.Throws<ArgumentException>(() => VerdictEvaluator.Evaluate(result, _vt));
        Assert.Throws<ArgumentException>(() => VerdictEvaluator.EvaluateDeviceOpen(result, "iHD"));
    }

    /// <summary>A device open ignores the always-non-zero exit code and matches the driver name.</summary>
    /// <param name="stderr">The device-open stderr.</param>
    /// <param name="expected">The expected outcome.</param>
    [Theory]
    [InlineData("VAAPI driver: Intel iHD driver for Intel(R) Gen Graphics - 24.1.0 ().\n", ProbeOutcome.Pass)]
    [InlineData("VAAPI driver: Intel i965 driver for Intel(R) Coffee Lake - 2.4.1.\n", ProbeOutcome.DeviceUnavailable)]
    [InlineData("Device creation failed: -12.\n", ProbeOutcome.DeviceUnavailable)]
    [InlineData("Failed to set value 'vaapi=va:/dev/dri/renderD128' for option 'init_hw_device': Permission denied\n", ProbeOutcome.PermissionDenied)]
    public void DeviceOpenMatchesDriverName(string stderr, ProbeOutcome expected) =>
        Assert.Equal(expected, VerdictEvaluator.EvaluateDeviceOpen(Exited(1, null, stderr), "iHD"));

    /// <summary>A timed-out device open is a timeout.</summary>
    [Fact]
    public void DeviceOpenTimeout() =>
        Assert.Equal(ProbeOutcome.Timeout, VerdictEvaluator.EvaluateDeviceOpen(Run(FfmpegRunStatus.TimedOut, null, null, string.Empty), "iHD"));

    /// <summary>A short log is returned unchanged.</summary>
    [Fact]
    public void ShortTailUnchanged() => Assert.Equal("abc", VerdictEvaluator.StderrTail("abc"));

    /// <summary>A long log keeps its end, within the byte bound.</summary>
    [Fact]
    public void LongTailKeepsEnd()
    {
        var tail = VerdictEvaluator.StderrTail(new string('a', 10_000) + "THE END");

        Assert.EndsWith("THE END", tail, StringComparison.Ordinal);
        Assert.Equal(VerdictEvaluator.DefaultTailBytes, System.Text.Encoding.UTF8.GetByteCount(tail));
    }

    /// <summary>The cut never splits a multi-byte character.</summary>
    [Fact]
    public void TailCutsOnCharacterBoundary() =>
        Assert.Equal("éé", VerdictEvaluator.StderrTail("éééé", 5));

    /// <summary>Builds an exited run.</summary>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="frames">The final frame count.</param>
    /// <param name="stderr">The stderr.</param>
    /// <returns>The run result.</returns>
    private static FfmpegRunResult Exited(int exitCode, long? frames, string stderr) =>
        Run(FfmpegRunStatus.Exited, exitCode, frames, stderr);

    /// <summary>Builds a run result.</summary>
    /// <param name="status">How the run ended.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="frames">The final frame count.</param>
    /// <param name="stderr">The stderr.</param>
    /// <returns>The run result.</returns>
    private static FfmpegRunResult Run(FfmpegRunStatus status, int? exitCode, long? frames, string stderr) =>
        new(status, exitCode, string.Empty, stderr, frames, TimeSpan.Zero, null);
}
