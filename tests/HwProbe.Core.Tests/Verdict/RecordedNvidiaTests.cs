using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;

/// <summary>Real jellyfin-ffmpeg 8.1.2 output from an RTX 5080 in the official Jellyfin 12.1 container.</summary>
[Trait("Category", "Unit")]
public sealed class RecordedNvidiaTests
{
    /// <summary>CUDA decode, CUDA filters and NVENC encode pass, with BWDIF and without.</summary>
    /// <param name="file">The recorded stderr.</param>
    [Theory]
    [InlineData("stderr/jellyfin-linux-nvenc-smoke-pass.txt")]
    [InlineData("stderr/jellyfin-linux-nvenc-bwdif-pass.txt")]
    public void HardwareTranscodePasses(string file) =>
        Assert.Equal(
            ProbeOutcome.Pass,
            VerdictEvaluator.Evaluate(
                new(FfmpegRunStatus.Exited, 0, string.Empty, CorpusFile.Load(file), MatrixCatalog.Frames, TimeSpan.Zero, null),
                new ProbeExpectation(MatrixCatalog.Frames, StderrMarkers.HardwareFrames(HwType.nvenc, "cuda"))));
}
