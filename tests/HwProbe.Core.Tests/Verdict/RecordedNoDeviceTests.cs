using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;

/// <summary>Real jellyfin-ffmpeg output from hosts with no hardware, classified as device failures.</summary>
[Trait("Category", "Unit")]
public sealed class RecordedNoDeviceTests
{
    /// <summary>A failed device open is DeviceUnavailable.</summary>
    /// <param name="file">The recorded stderr.</param>
    /// <param name="type">The backend probed.</param>
    [Theory]
    [InlineData("stderr/jellyfin-linux-cuda-no-device.txt", HwType.nvenc)]
    [InlineData("stderr/jellyfin-windows-cuda-no-device.txt", HwType.nvenc)]
    [InlineData("stderr/jellyfin-windows-d3d11-no-adapter.txt", HwType.qsv)]
    [InlineData("stderr/jellyfin-windows-d3d11-no-adapter.txt", HwType.amf)]
    public void DeviceOpenFails(string file, HwType type) =>
        Assert.Equal(ProbeOutcome.DeviceUnavailable, DeviceOpenProbe.Evaluate(type, Exited(255, CorpusFile.Load(file))).Outcome);

    /// <summary>rkmpp's device open succeeds without hardware, which is why its smoke probe has to catch it.</summary>
    [Fact]
    public void RkmppOpenPassesWithoutDevice() =>
        Assert.Equal(ProbeOutcome.Pass, DeviceOpenProbe.Evaluate(HwType.rkmpp, Exited(1, CorpusFile.Load("stderr/jellyfin-linux-rkmpp-open-without-device.txt"))).Outcome);

    /// <summary>A smoke probe that finds no device is DeviceUnavailable, not a codec failure.</summary>
    /// <param name="file">The recorded stderr.</param>
    /// <param name="type">The backend probed.</param>
    [Theory]
    [InlineData("stderr/jellyfin-linux-rkmpp-smoke-no-device.txt", HwType.rkmpp)]
    [InlineData("stderr/jellyfin-linux-v4l2m2m-no-device.txt", HwType.v4l2m2m)]
    public void SmokeProbeFindsNoDevice(string file, HwType type) =>
        Assert.Equal(
            ProbeOutcome.DeviceUnavailable,
            VerdictEvaluator.Evaluate(Exited(234, CorpusFile.Load(file)), new ProbeExpectation(MatrixCatalog.Frames, StderrMarkers.HardwareFrames(type))));

    /// <summary>Wraps recorded stderr as an exited run.</summary>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="stderr">The stderr text.</param>
    /// <returns>The run result.</returns>
    private static FfmpegRunResult Exited(int exitCode, string stderr) =>
        new(FfmpegRunStatus.Exited, exitCode, string.Empty, stderr, 0, TimeSpan.Zero, null);
}
