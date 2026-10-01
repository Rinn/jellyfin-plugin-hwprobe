using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;

/// <summary>Real jellyfin-ffmpeg 8.1.3 output from an Intel Apollo Lake host (iHD 26.3.5).</summary>
[Trait("Category", "Unit")]
public sealed class RecordedIntelTests
{
    /// <summary>Hardware transcodes that exit 0 with frames are passes.</summary>
    /// <param name="file">The recorded stderr.</param>
    /// <param name="type">The backend probed.</param>
    /// <param name="hwaccel">The <c>-hwaccel</c> in the recorded command.</param>
    [Theory]
    [InlineData("stderr/jellyfin-linux-qsv-over-vaapi-pass.txt", HwType.qsv, "vaapi")]
    [InlineData("stderr/jellyfin-linux-vaapi-vc1-pass.txt", HwType.vaapi, "vaapi")]
    public void HardwareTranscodePasses(string file, HwType type, string hwaccel) =>
        Assert.Equal(
            ProbeOutcome.Pass,
            VerdictEvaluator.Evaluate(Completed(CorpusFile.Load(file)), new ProbeExpectation(MatrixCatalog.Frames, StderrMarkers.HardwareFrames(type, hwaccel))));

    /// <summary>Without the hwaccel's format, QSV decoded through VAAPI is not confirmed.</summary>
    [Fact]
    public void QsvOverVaapiNeedsHwaccelFormat() =>
        Assert.Equal(
            ProbeOutcome.SoftwareFallback,
            VerdictEvaluator.Evaluate(Completed(CorpusFile.Load("stderr/jellyfin-linux-qsv-over-vaapi-pass.txt")), new ProbeExpectation(MatrixCatalog.Frames, StderrMarkers.HardwareFrames(HwType.qsv))));

    /// <summary>A codec the GPU can't decode is CodecUnsupported, though the hardware filters fail too.</summary>
    [Fact]
    public void UndecodableCodecIsCodecUnsupported() =>
        Assert.Equal(
            ProbeOutcome.CodecUnsupported,
            VerdictEvaluator.Evaluate(new(FfmpegRunStatus.Exited, 69, string.Empty, CorpusFile.Load("stderr/jellyfin-linux-vaapi-av1-unsupported.txt"), 0, TimeSpan.Zero, null), new ProbeExpectation(MatrixCatalog.Frames, StderrMarkers.HardwareFrames(HwType.vaapi, "vaapi"))));

    /// <summary>Deriving OpenCL with no OpenCL runtime installed is DeviceUnavailable.</summary>
    [Fact]
    public void OpenclWithoutRuntimeIsUnavailable() =>
        Assert.Equal(
            ProbeOutcome.DeviceUnavailable,
            DeviceOpenProbe.EvaluateOpencl(new(FfmpegRunStatus.Exited, 237, string.Empty, CorpusFile.Load("stderr/jellyfin-linux-opencl-no-runtime.txt"), null, TimeSpan.Zero, null)));

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

    /// <summary>Wraps recorded stderr as a run that exited 0 with the expected frames.</summary>
    /// <param name="stderr">The stderr text.</param>
    /// <returns>The run result.</returns>
    private static FfmpegRunResult Completed(string stderr) =>
        new(FfmpegRunStatus.Exited, 0, string.Empty, stderr, MatrixCatalog.Frames, TimeSpan.Zero, null);
}
