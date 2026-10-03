using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Full-quality arguments for speed runs, from EncodingHelper 12.1.0's software path, which every OS takes.</summary>
[Trait("Category", "Unit")]
[Collection(TestCollections.EncodingHelperEnvironment)]
public sealed class SpeedArgumentsTests
{
    private static readonly ProbeCell _cell = new("h264", 8, "h264", HardwareDecode: true, HardwareEncode: true)
    {
        FullQuality = true,
        Audio = true,
        SourceWidth = 1920,
        SourceHeight = 1080,
        SourceFrameRate = 24,
        MaxWidth = 1280,
        MaxHeight = 720,
        VideoBitrate = 4_000_000,
        SourcePath = "/c/a.mkv",
    };

    /// <summary>The encoder gets the preset, CRF and bitrate cap; audio is stereo AAC at upstream's default; the input is upstream's.</summary>
    [Fact]
    public void SoftwareGetsEverythingARequestDoes()
    {
        var args = Build(_cell);

        Assert.Equal("libx264", args.VideoEncoder);
        Assert.StartsWith(" -preset veryfast -crf 23 -maxrate 4000000 -bufsize 8000000", args.EncoderArgs, StringComparison.Ordinal);
        Assert.StartsWith(" -codec:a:0 aac -ac 2 -ab 256000", args.AudioArgs, StringComparison.Ordinal);
        Assert.Equal(" -i file:\"/c/a.mkv\"", args.InputArgument);
    }

    /// <summary>The size comes from the bitrate and codec as the server picks it: H.264 needs more than 6 Mbps for 1080p, HEVC doesn't.</summary>
    /// <param name="codec">The output codec.</param>
    /// <param name="bitrate">The bitrate asked for.</param>
    /// <param name="width">The width the output is limited to.</param>
    [Theory]
    [InlineData("h264", 8_000_000, 1920)]
    [InlineData("h264", 6_000_000, 1280)]
    [InlineData("hevc", 6_000_000, 1920)]
    [InlineData("h264", 1_500_000, 1280)]
    [InlineData("h264", 720_000, 960)]
    public void BitrateSetsTheSize(string codec, int bitrate, int width)
    {
        var args = Build(_cell with { OutputCodec = codec, MaxWidth = null, MaxHeight = null, VideoBitrate = bitrate });

        Assert.Equal(width, args.OutputWidth);
    }

    /// <summary>A library path's quotes are escaped as upstream does, so they can't end the argument.</summary>
    [Fact]
    public void LibraryPathIsEscaped() =>
        Assert.Equal(" -i file:\"/m/x\\\" -y \\\"evil.mkv\"", Build(_cell with { SourcePath = "/m/x\" -y \"evil.mkv" }).InputArgument);

    /// <summary>Copied audio is passed through, as for a client that plays the source's audio.</summary>
    [Fact]
    public void CopiedAudioIsPassedThrough() =>
        Assert.Equal(" -codec:a:0 copy", Build(_cell with { AudioCopy = true }).AudioArgs);

    /// <summary>The server's preset and CRF reach the encoder.</summary>
    [Fact]
    public void PresetAndCrfReachTheEncoder() =>
        Assert.StartsWith(" -preset faster -crf 18 ", Build(_cell with { EncoderPreset = "faster", H264Crf = 18 }).EncoderArgs, StringComparison.Ordinal);

    /// <summary>VBR audio changes the audio arguments only with an encoder upstream has a VBR mode for.</summary>
    [Fact]
    public void VbrNeedsAnEncoderWithAVbrMode()
    {
        var fdk = TestCapabilities.Full with { Encoders = new HashSet<string>(TestCapabilities.Full.Encoders) { "libfdk_aac" } };

        Assert.Equal(Build(_cell).AudioArgs, Build(_cell with { AudioVbr = true }).AudioArgs);
        Assert.Contains("-vbr:a", Build(_cell with { AudioVbr = true }, fdk).AudioArgs, StringComparison.Ordinal);
    }

    /// <summary>An image subtitle is a second input, overlaid on the video.</summary>
    [Fact]
    public void ImageSubtitleIsASecondInput()
    {
        var args = Build(_cell with { GraphicalSubtitlePath = "/c/s.sup" });

        Assert.Equal(" -i file:\"/c/a.mkv\" -i file:\"/c/s.sup\"", args.InputArgument);
        Assert.Contains("overlay", args.FilterArgs, StringComparison.Ordinal);
    }

    /// <summary>A subtitle inside the file is overlaid from its own stream, whatever tracks come before it.</summary>
    [Fact]
    public void InternalImageSubtitleUsesItsStream() =>
        Assert.Contains("[0:3]", Build(_cell with { InternalSubtitleIndex = 3, InternalSubtitleCodec = "PGSSUB" }).FilterArgs, StringComparison.Ordinal);

    /// <summary>The thread count is upstream's: automatic by default, the server's setting capped at the core count otherwise.</summary>
    [Fact]
    public void ThreadsFollowTheSetting()
    {
        Assert.Equal(0, Build(_cell).Threads);
        Assert.Equal(Math.Min(2, Environment.ProcessorCount), Build(_cell with { EncodingThreadCount = 2 }).Threads);
    }

    /// <summary>Double-rate deinterlacing changes the deinterlace filter.</summary>
    [Fact]
    public void DoubleRateChangesTheFilter()
    {
        var interlaced = _cell with { Interlaced = true };

        Assert.NotEqual(Build(interlaced).FilterArgs, Build(interlaced with { DoubleRate = true }).FilterArgs);
    }

    /// <summary>Generates software arguments.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="capabilities">The build, or the full test build.</param>
    /// <returns>The arguments.</returns>
    private static ProbeArguments Build(ProbeCell cell, ProbeCapabilities? capabilities = null) =>
        new ArgumentSource(capabilities ?? TestCapabilities.Full, new CallRecorder()).Build(HwType.none, null, cell);
}
