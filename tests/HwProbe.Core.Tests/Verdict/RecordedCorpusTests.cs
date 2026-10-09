using System.Reflection;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;

/// <summary>Real ffmpeg output in <c>tests/Corpus/stderr</c>: Intel Apollo Lake (iHD 26.3.5, jellyfin-ffmpeg 8.1.3), RTX 5080 (jellyfin-ffmpeg 8.1.2 in Jellyfin 12.1), Rockchip RK3588S (jellyfin-ffmpeg 8.1.3 in Jellyfin 12.2), Raspberry Pi V4L2, macOS VideoToolbox, and hosts with no hardware.</summary>
[Trait("Category", "Unit")]
public sealed class RecordedCorpusTests
{
    private const string OpenclNoRuntime = "stderr/jellyfin-linux-opencl-no-runtime.txt";

    /// <summary>A recorded transcode is scored by the three-condition rule and the failure classification.</summary>
    /// <param name="file">The recorded stderr.</param>
    /// <param name="type">The backend probed.</param>
    /// <param name="hwaccel">The <c>-hwaccel</c> in the recorded command, or null to confirm by the backend's own format only.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="frames">The final frame count.</param>
    /// <param name="expected">The outcome.</param>
    [Theory]
    [InlineData("stderr/jellyfin-linux-qsv-over-vaapi-pass.txt", HwType.qsv, "vaapi", 0, MatrixCatalog.Frames, ProbeOutcome.Pass)]
    [InlineData("stderr/jellyfin-linux-qsv-over-vaapi-pass.txt", HwType.qsv, null, 0, MatrixCatalog.Frames, ProbeOutcome.SoftwareFallback)]
    [InlineData("stderr/jellyfin-linux-vaapi-vc1-pass.txt", HwType.vaapi, "vaapi", 0, MatrixCatalog.Frames, ProbeOutcome.Pass)]
    [InlineData("stderr/jellyfin-linux-vaapi-av1-unsupported.txt", HwType.vaapi, "vaapi", 69, 0, ProbeOutcome.CodecUnsupported)]
    [InlineData("stderr/jellyfin-linux-qsv-hevc-lowpower-disabled.txt", HwType.qsv, "vaapi", 0, MatrixCatalog.Frames, ProbeOutcome.SoftwareFallback)]
    [InlineData("stderr/jellyfin-linux-nvenc-smoke-pass.txt", HwType.nvenc, "cuda", 0, MatrixCatalog.Frames, ProbeOutcome.Pass)]
    [InlineData("stderr/jellyfin-linux-nvenc-bwdif-pass.txt", HwType.nvenc, "cuda", 0, MatrixCatalog.Frames, ProbeOutcome.Pass)]
    [InlineData("stderr/jellyfin-linux-rkmpp-smoke-no-device.txt", HwType.rkmpp, null, 234, 0, ProbeOutcome.DeviceUnavailable)]
    [InlineData("stderr/jellyfin-linux-rkmpp-rk3588s-smoke-pass.txt", HwType.rkmpp, "rkmpp", 0, MatrixCatalog.Frames, ProbeOutcome.Pass)]
    [InlineData("stderr/jellyfin-linux-v4l2m2m-no-device.txt", HwType.v4l2m2m, null, 234, 0, ProbeOutcome.DeviceUnavailable)]
    [InlineData("stderr/v4l2m2m-bcm2835-h264-pass.txt", HwType.v4l2m2m, null, 0, MatrixCatalog.Frames, ProbeOutcome.Pass)]
    [InlineData("stderr/videotoolbox-h264-pass.txt", HwType.videotoolbox, null, 0, MatrixCatalog.Frames, ProbeOutcome.Pass)]
    [InlineData("stderr/videotoolbox-mjpeg-pass.txt", HwType.videotoolbox, null, 0, MatrixCatalog.Frames, ProbeOutcome.Pass)]
    [InlineData("stderr/videotoolbox-mpeg4-sw-fallback.txt", HwType.videotoolbox, null, 0, MatrixCatalog.Frames, ProbeOutcome.SoftwareFallback)]
    public void Evaluate(string file, HwType type, string? hwaccel, int exitCode, long frames, ProbeOutcome expected) =>
        Assert.Equal(expected, VerdictEvaluator.Evaluate(Exited(exitCode, frames, CorpusFile.Load(file)), new ProbeExpectation(MatrixCatalog.Frames, StderrMarkers.HardwareFrames(type, hwaccel))));

    /// <summary>A recorded device open is scored by its stderr; rkmpp's succeeds without hardware, which is why its smoke probe has to catch it.</summary>
    /// <param name="file">The recorded stderr.</param>
    /// <param name="type">The backend probed.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="expected">The outcome.</param>
    [Theory]
    [InlineData("stderr/jellyfin-linux-cuda-no-device.txt", HwType.nvenc, 255, ProbeOutcome.DeviceUnavailable)]
    [InlineData("stderr/jellyfin-windows-cuda-no-device.txt", HwType.nvenc, 255, ProbeOutcome.DeviceUnavailable)]
    [InlineData("stderr/jellyfin-windows-d3d11-no-adapter.txt", HwType.qsv, 255, ProbeOutcome.DeviceUnavailable)]
    [InlineData("stderr/jellyfin-windows-d3d11-no-adapter.txt", HwType.amf, 255, ProbeOutcome.DeviceUnavailable)]
    [InlineData("stderr/jellyfin-linux-rkmpp-open-without-device.txt", HwType.rkmpp, 1, ProbeOutcome.Pass)]
    [InlineData("stderr/jellyfin-linux-vaapi-rockchip-no-driver.txt", HwType.vaapi, 251, ProbeOutcome.DeviceUnavailable)]
    public void DeviceOpen(string file, HwType type, int exitCode, ProbeOutcome expected) =>
        Assert.Equal(expected, DeviceOpenProbe.Evaluate(type, Exited(exitCode, 0, CorpusFile.Load(file))).Outcome);

    /// <summary>A VAAPI open that reached libva but found no driver names the driver file; one that loaded a driver names none.</summary>
    /// <param name="file">The recorded stderr.</param>
    /// <param name="expected">The driver file, or null.</param>
    [Theory]
    [InlineData("stderr/jellyfin-linux-vaapi-rockchip-no-driver.txt", "rockchip_drv_video.so")]
    [InlineData("stderr/jellyfin-linux-qsv-over-vaapi-pass.txt", null)]
    public void MissingVaapiDriverIsNamed(string file, string? expected) =>
        Assert.Equal(expected, DeviceOpenProbe.MissingVaapiDriver(CorpusFile.Load(file)));

    /// <summary>Deriving OpenCL with no OpenCL runtime installed is DeviceUnavailable.</summary>
    [Fact]
    public void OpenclWithoutRuntimeIsUnavailable() =>
        Assert.Equal(ProbeOutcome.DeviceUnavailable, DeviceOpenProbe.EvaluateOpencl(new(FfmpegRunStatus.Exited, 237, string.Empty, CorpusFile.Load(OpenclNoRuntime), null, TimeSpan.Zero, null)));

    /// <summary>Lines the rows above depend on: warnings a pass tolerates, the low-power marker, and the V4L2 device line only a real device logs.</summary>
    /// <param name="file">The recorded stderr.</param>
    /// <param name="line">The text looked for.</param>
    /// <param name="present">Whether the file contains it.</param>
    [Theory]
    [InlineData("stderr/v4l2m2m-bcm2835-h264-pass.txt", "Failed to set frame level rate control", true)]
    [InlineData("stderr/v4l2m2m-bcm2835-h264-pass.txt", StderrMarkers.V4l2Device, true)]
    [InlineData("stderr/jellyfin-linux-v4l2m2m-no-device.txt", StderrMarkers.V4l2Device, false)]
    [InlineData("stderr/videotoolbox-mjpeg-pass.txt", "is not supported on this device", true)]
    [InlineData("stderr/jellyfin-linux-rkmpp-rk3588s-smoke-pass.txt", "Failed to get packet from encoder output queue: -11", true)]
    public void RecordedLines(string file, string line, bool present) =>
        Assert.Equal(present, CorpusFile.Load(file).Contains(line, StringComparison.Ordinal));

    /// <summary>hevc_qsv dropping low-power mode logs the marker that the <see cref="Evaluate"/> row scores as a software fallback.</summary>
    [Fact]
    public void QsvLowPowerDisabledIsRecognised()
    {
        var stderr = CorpusFile.Load("stderr/jellyfin-linux-qsv-hevc-lowpower-disabled.txt");

        Assert.Contains(StderrMarkers.LowPowerDisabled, m => stderr.Contains(m, StringComparison.Ordinal));
    }

    /// <summary>OpenCL is derived from the VAAPI device for VAAPI and QSV on Linux only.</summary>
    [Fact]
    public void OpenclArgumentsDeriveFromVaapi()
    {
        const string Expected = "-v verbose -hide_banner -init_hw_device vaapi=va:/dev/dri/renderD128 -init_hw_device opencl=ocl@va";

        Assert.Equal(Expected, DeviceOpenProbe.OpenclArguments(HwType.vaapi, "/dev/dri/renderD128", HostOs.Linux));
        Assert.Equal(Expected, DeviceOpenProbe.OpenclArguments(HwType.qsv, "/dev/dri/renderD128", HostOs.Linux));
        Assert.Null(DeviceOpenProbe.OpenclArguments(HwType.qsv, "0", HostOs.Windows));
        Assert.Null(DeviceOpenProbe.OpenclArguments(HwType.nvenc, "0", HostOs.Linux));
    }

    /// <summary>Every recorded stderr file is scored by a row here.</summary>
    [Fact]
    public void EveryRecordedFileIsCovered()
    {
        var covered = new[] { nameof(Evaluate), nameof(DeviceOpen) }
            .SelectMany(m => typeof(RecordedCorpusTests).GetMethod(m)?.GetCustomAttributes<InlineDataAttribute>() ?? throw new InvalidOperationException($"No test method {m}."))
            .Select(a => a.Data[0])
            .OfType<string>()
            .Append(OpenclNoRuntime)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);
        var recorded = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Corpus", "stderr")).Select(f => Path.GetFileName(f));

        Assert.All(recorded, f => Assert.Contains(f, covered));
    }

    /// <summary>Wraps recorded stderr as an exited run.</summary>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="frames">The final frame count.</param>
    /// <param name="stderr">The stderr text.</param>
    /// <returns>The run result.</returns>
    private static FfmpegRunResult Exited(int exitCode, long frames, string stderr) =>
        new(FfmpegRunStatus.Exited, exitCode, string.Empty, stderr, frames, TimeSpan.Zero, null);
}
