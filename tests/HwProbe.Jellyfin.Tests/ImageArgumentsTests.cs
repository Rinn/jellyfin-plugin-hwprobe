using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Image extraction arguments: EncodingHelper 12.2.0's input, filter and encoder, inside the pinned copy of what MediaEncoder adds around them.</summary>
/// <remarks>A failure means upstream changed what it emits or how MediaEncoder wraps it: compare with the "Trickplay generation" line Jellyfin logs (container-plugin.sh checks it), then update.</remarks>
[Trait("Category", "Unit")]
[Collection(TestCollections.EncodingHelperEnvironment)]
public sealed class ImageArgumentsTests
{
    private static readonly ProbeCell _cell = new("h264", 8, "mjpeg", HardwareDecode: true, HardwareEncode: true)
    {
        SourceWidth = 1920,
        SourceHeight = 1080,
        SourceFrameRate = 24,
        SourcePath = "/c/a.mkv",
    };

    private static readonly ImageJob _job = new(320, 10000, 4, 1, HwEncoding: false, KeyFramesOnly: false);

    /// <summary>Software gets the input threads before the input, the stream map after it, setpts before the fps filter, and the quality scale.</summary>
    [Fact]
    public void SoftwareIsPinned()
    {
        var args = Build(HwType.none, _job);

        Assert.Equal("-threads 1 -i file:\"/c/a.mkv\" -map 0:0", args.InputArgument);
        Assert.Equal("-vf \"setpts=N/24.000/TB,fps=0.10000000149011612,setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709,scale=trunc(min(max(iw\\,ih*(a*sar))\\,320)/2)*2:trunc(ow/(a*sar)/2)*2,format=yuv420p\"", args.FilterArgs);
        Assert.Equal("mjpeg", args.VideoEncoder);
        Assert.Equal("-qscale:v 4 -fps_mode passthrough", args.EncoderArgs);
        Assert.Equal(1, args.Threads);
        Assert.False(args.HardwareEncoder);
    }

    /// <summary>Key frames only skips the other frames in the decoder and leaves timestamps alone, so no setpts.</summary>
    [Fact]
    public void KeyFramesOnlySkipsFrames()
    {
        var args = Build(HwType.none, _job with { KeyFramesOnly = true, Threads = 2, Qscale = 10 });

        Assert.StartsWith("-skip_frame nokey -threads 2 -i ", args.InputArgument, StringComparison.Ordinal);
        Assert.DoesNotContain("setpts", args.FilterArgs, StringComparison.Ordinal);
        Assert.Equal("-qscale:v 10 -fps_mode passthrough", args.EncoderArgs);
        Assert.Equal(2, args.Threads);
    }

    /// <summary>VAAPI and QSV take JPEG quality, VideoToolbox quality scaled to 118, and RKMPP JPEG quality to 99, each with upstream's integer steps.</summary>
    /// <param name="encoder">The MJPEG encoder.</param>
    /// <param name="qscale">The quality scale.</param>
    /// <param name="option">The option expected.</param>
    /// <param name="value">The value expected.</param>
    [Theory]
    [InlineData("mjpeg", 4, "-qscale:v ", 4)]
    [InlineData("mjpeg", 40, "-qscale:v ", 31)]
    [InlineData("mjpeg_vaapi", 4, "-global_quality:v ", 91)]
    [InlineData("mjpeg_qsv", 31, "-global_quality:v ", 10)]
    [InlineData("mjpeg_videotoolbox", 4, "-qscale:v ", 109)]
    [InlineData("mjpeg_rkmpp", 4, "-qp_init:v ", 90)]
    public void QualityFollowsTheEncoder(string encoder, int qscale, string option, int value) =>
        Assert.Equal((option, value), ArgumentSource.ImageQuality(encoder, qscale));

    /// <summary>A backend whose decoders don't skip to key frames, with the setting upstream checks, makes the images in software.</summary>
    /// <param name="type">The backend.</param>
    [Theory]
    [InlineData(HwType.nvenc)]
    [InlineData(HwType.qsv)]
    public void KeyFramesOnlyNeedsADecoderThatSkips(HwType type) =>
        Assert.Throws<ArgumentConstructionException>(() => Build(type, _job with { KeyFramesOnly = true }, _cell with { EnhancedNvdec = false, PreferNativeDecoder = false }));

    /// <summary>VideoToolbox decodes on the GPU, at low priority when the build takes the flag, and encodes with its MJPEG encoder when asked, allowing its software fallback.</summary>
    [Fact(Skip = "Requires macOS: EncodingHelper only takes its VideoToolbox branch there.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    public void VideoToolboxDecodesAndEncodes()
    {
        var capabilities = TestCapabilities.Full with { Encoders = new HashSet<string>(TestCapabilities.Full.Encoders) { "mjpeg_videotoolbox" } };
        var args = new ArgumentSource(capabilities, new CallRecorder()) { LowPriorityHwDecode = true }.BuildImages(HwType.videotoolbox, null, _cell, _job with { HwEncoding = true });

        Assert.StartsWith("-hwaccel_flags +low_priority ", args.InputArgument, StringComparison.Ordinal);
        Assert.Contains("-hwaccel videotoolbox", args.InputArgument, StringComparison.Ordinal);
        Assert.Equal("mjpeg_videotoolbox", args.VideoEncoder);
        Assert.Equal("-qscale:v 109 -allow_sw 1 -fps_mode passthrough", args.EncoderArgs);
        Assert.True(args.HardwareEncoder);
    }

    /// <summary>Generates image arguments over the full test build.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="job">The image settings.</param>
    /// <param name="cell">The video, or the 1080p H.264 one.</param>
    /// <returns>The arguments.</returns>
    private static ProbeArguments Build(HwType type, ImageJob job, ProbeCell? cell = null) =>
        new ArgumentSource(TestCapabilities.Full, new CallRecorder()).BuildImages(type, null, cell ?? _cell, job);
}
