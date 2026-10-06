using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary><see cref="ArgumentSource"/> rules whose EncodingHelper branches depend on the OS.</summary>
/// <remarks>Generation mutates process env, so every class that generates args joins <see cref="TestCollections.EncodingHelperEnvironment"/>.</remarks>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
[Collection(TestCollections.EncodingHelperEnvironment)]
public sealed class ArgumentSourcePlatformTests
{
    private readonly CallRecorder _recorder = new();

    /// <summary>VideoToolbox smoke probe: device init and decoder args match the reference table; scaling is hardware.</summary>
    [Fact(Skip = "Requires macOS: EncodingHelper only emits VideoToolbox args there.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    public void VideoToolboxSmoke()
    {
        var args = Build(HwType.videotoolbox, null, ArgumentSourceCells.Smoke);

        Assert.Equal("-init_hw_device videotoolbox=vt -hwaccel videotoolbox -hwaccel_output_format videotoolbox_vld -noautorotate", args.InputArgs);
        Assert.Equal(" -vf \"scale_vt=w=320:h=180\"", args.FilterArgs);
        Assert.Equal("h264_videotoolbox", args.VideoEncoder);
        Assert.Equal(EncodingHelperEnvironment.Capture(), args.Environment);
        Assert.NotNull(args.HardwareDecoder);
        Assert.True(args.HardwareEncoder);
    }

    /// <summary>VideoToolbox tone-maps inside scale_vt rather than with a separate filter.</summary>
    [Fact(Skip = "Requires macOS: EncodingHelper only emits VideoToolbox args there.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    public void VideoToolboxHdr10TonemapsInScaleVt()
    {
        var args = Build(HwType.videotoolbox, null, ArgumentSourceCells.Hdr10);

        Assert.Equal(" -vf \"scale_vt=format=nv12:color_matrix=bt709:color_primaries=bt709:color_transfer=bt709\"", args.FilterArgs);
        Assert.True(args.HardwareTonemap);
    }

    /// <summary>EncodingHelper's arguments, including bitrates, frame rates and tone mapping numbers, read the same in every culture.</summary>
    /// <param name="culture">The current culture.</param>
    [Theory(Skip = "Requires macOS: EncodingHelper only emits VideoToolbox args there.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    [MemberData(nameof(CultureScope.Different), MemberType = typeof(CultureScope))]
    public void SameInEveryCulture(string culture)
    {
        var cell = ArgumentSourceCells.Hdr10 with { VideoBitrate = 4_500_000, SourceFrameRate = 23.976f, MaxWidth = 1280, TonemapPeak = 400.5, TonemapDesat = 0.5, Audio = true, FullQuality = true, SourcePath = "/c/a.mkv", DownmixBoost = 1.5 };
        static string Text(ProbeArguments a) => string.Join('|', a.InputArgs, a.FilterArgs, a.VideoEncoder, a.EncoderArgs, a.AudioArgs, string.Join(';', a.Environment.Select(e => e.Key + "=" + e.Value)));
        var invariant = Text(Build(HwType.none, null, cell));
        using var scope = new CultureScope(culture);

        Assert.Equal(invariant, Text(Build(HwType.none, null, cell)));
        Assert.Contains("-maxrate 4500000", invariant, StringComparison.Ordinal);
        Assert.Contains("volume=1.5", invariant, StringComparison.Ordinal);
    }

    /// <summary>Encode-only VideoToolbox emits the device init without a hardware decoder.</summary>
    [Fact(Skip = "Requires macOS: EncodingHelper only emits VideoToolbox args there.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    public void VideoToolboxEncodeOnly()
    {
        var args = Build(HwType.videotoolbox, null, new ProbeCell("h264", 8, "hevc", HardwareDecode: false, HardwareEncode: true));

        Assert.Equal("-init_hw_device videotoolbox=vt", args.InputArgs);
        Assert.Equal("hevc_videotoolbox", args.VideoEncoder);
    }

    /// <summary>Neither decoder nor encoder on the backend makes EncodingHelper return empty, which must not become a runnable probe.</summary>
    /// <param name="type">A backend that normally emits args.</param>
    [Theory]
    [InlineData(HwType.vaapi)]
    [InlineData(HwType.qsv)]
    [InlineData(HwType.nvenc)]
    [InlineData(HwType.videotoolbox)]
    public void EmptyArgsIsConstructionError(HwType type)
    {
        var source = new ArgumentSource(TestCapabilities.Full, _recorder);
        var softwareOnly = new ProbeCell("h264", 8, "h264", HardwareDecode: false, HardwareEncode: false);

        Assert.Throws<ArgumentConstructionException>(() => source.Build(type, ArgumentSourceCells.Node, softwareOnly));
    }

    /// <summary>A build with no hardware encoder for the backend reports a software encoder.</summary>
    [Fact(Skip = "Requires macOS: EncodingHelper only emits VideoToolbox args there.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    public void MissingHardwareEncoderIsSoftware()
    {
        var caps = TestCapabilities.Full with { Encoders = new HashSet<string>(TestCapabilities.Full.Encoders.Where(e => e != "h264_videotoolbox")) };

        var args = new ArgumentSource(caps, _recorder).Build(HwType.videotoolbox, null, ArgumentSourceCells.Smoke);

        Assert.False(args.HardwareEncoder);
    }

    /// <summary>Generation reaches only modelled IMediaEncoder members; the other five dependencies are never touched.</summary>
    [Fact]
    public void OnlyModelledMembersAreReached()
    {
        var source = new ArgumentSource(TestCapabilities.Full, _recorder);
        foreach (var type in Enum.GetValues<HwType>().Where(t => t != HwType.none))
        {
            foreach (var cell in new[] { ArgumentSourceCells.Smoke, ArgumentSourceCells.Hdr10 })
            {
                try
                {
                    source.Build(type, ArgumentSourceCells.Node, cell);
                }
                catch (ArgumentConstructionException)
                {
                    // Off-platform backends emit nothing; the calls made getting there still count.
                }
            }
        }

        Assert.Empty(_recorder.Unexpected);
        Assert.All(_recorder.Calls, c => Assert.StartsWith("IMediaEncoder.", c, StringComparison.Ordinal));
    }

    /// <summary>Builds arguments with the full capability set, asserting no unmodelled member was reached.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device, or null.</param>
    /// <param name="cell">The media shape.</param>
    /// <returns>The generated arguments.</returns>
    private ProbeArguments Build(HwType type, string? device, ProbeCell cell) => ArgumentSourceCells.Build(_recorder, type, device, cell);
}
