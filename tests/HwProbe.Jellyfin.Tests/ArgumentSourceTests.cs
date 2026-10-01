using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Drift oracle and construction rules for <see cref="ArgumentSource"/> over EncodingHelper 12.1.0.</summary>
/// <remarks>Generation mutates process env, so every class that generates args joins <see cref="TestCollections.EncodingHelperEnvironment"/>.</remarks>
[Trait("Category", "Unit")]
[Collection(TestCollections.EncodingHelperEnvironment)]
public sealed class ArgumentSourceTests
{
    private const string Node = "/dev/dri/renderD128";

    private static readonly ProbeCell _smoke = new("h264", 8, "h264", HardwareDecode: true, HardwareEncode: true)
    {
        MaxWidth = 320,
        MaxHeight = 240,
    };

    private static readonly ProbeCell _hdr10 = new("hevc", 10, "h264", HardwareDecode: true, HardwareEncode: true)
    {
        Profile = "Main 10",
        ColorTransfer = "smpte2084",
        ColorPrimaries = "bt2020",
        ColorSpace = "bt2020nc",
        Tonemap = true,
    };

    private readonly CallRecorder _recorder = new();

    /// <summary>HwType values and names match upstream's HardwareAccelerationType exactly.</summary>
    [Fact]
    public void HwTypeMirrorsUpstreamEnum()
    {
        var ours = Enum.GetValues<HwType>().Select(v => (Name: v.ToString(), Value: (int)v));
        var upstream = Enum.GetValues<HardwareAccelerationType>().Select(v => (Name: v.ToString(), Value: (int)v));

        Assert.Equal(upstream, ours);
    }

    /// <summary>VideoToolbox smoke probe: device init and decoder args match the reference table; scaling is hardware.</summary>
    [Fact(Skip = "Requires macOS: EncodingHelper only emits VideoToolbox args there.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    public void VideoToolboxSmoke()
    {
        var args = Build(HwType.videotoolbox, null, _smoke);

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
        var args = Build(HwType.videotoolbox, null, _hdr10);

        Assert.Equal(" -vf \"scale_vt=format=nv12:color_matrix=bt709:color_primaries=bt709:color_transfer=bt709\"", args.FilterArgs);
        Assert.True(args.HardwareTonemap);
    }

    /// <summary>Encode-only VideoToolbox emits the device init without a hardware decoder.</summary>
    [Fact(Skip = "Requires macOS: EncodingHelper only emits VideoToolbox args there.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    public void VideoToolboxEncodeOnly()
    {
        var args = Build(HwType.videotoolbox, null, new ProbeCell("h264", 8, "hevc", HardwareDecode: false, HardwareEncode: true));

        Assert.Equal("-init_hw_device videotoolbox=vt", args.InputArgs);
        Assert.Equal("hevc_videotoolbox", args.VideoEncoder);
    }

    /// <summary>VAAPI on Linux: device init names the render node and the decoder outputs VAAPI surfaces.</summary>
    [Fact(Skip = "Requires Linux: EncodingHelper only emits VAAPI args there.", SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void VaapiSmoke()
    {
        var args = Build(HwType.vaapi, Node, _smoke);

        Assert.StartsWith("-init_hw_device vaapi=va", args.InputArgs, StringComparison.Ordinal);
        Assert.Contains("-hwaccel vaapi -hwaccel_output_format vaapi -noautorotate", args.InputArgs, StringComparison.Ordinal);
        Assert.Equal("h264_vaapi", args.VideoEncoder);
    }

    /// <summary>QSV on Linux derives the QSV device from a VAAPI parent and never uses child_device.</summary>
    [Fact(Skip = "Requires Linux: EncodingHelper only emits QSV-over-VAAPI args there.", SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void QsvDerivesFromVaapi()
    {
        var args = Build(HwType.qsv, Node, _smoke);

        Assert.Contains("-init_hw_device qsv=qs@va", args.InputArgs, StringComparison.Ordinal);
        Assert.DoesNotContain("child_device", args.InputArgs, StringComparison.Ordinal);
        Assert.Equal("h264_qsv", args.VideoEncoder);
    }

    /// <summary>i965 sets two env vars during generation; they are returned for the child and the parent is restored.</summary>
    [Fact(Skip = "Requires Linux: the i965 branch only runs there.", SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void I965EnvironmentIsReturnedAndRestored()
    {
        var before = Environment.GetEnvironmentVariable("LIBVA_DRIVER_NAME");
        var caps = TestCapabilities.Full with { IsVaapiDeviceInteliHD = false, IsVaapiDeviceInteli965 = true };

        var args = new ArgumentSource(caps, _recorder).Build(HwType.vaapi, Node, _smoke);

        Assert.Equal("i965", args.Environment["LIBVA_DRIVER_NAME"]);
        Assert.Equal("i965", args.Environment["LIBVA_DRIVER_NAME_JELLYFIN"]);
        Assert.Equal(before, Environment.GetEnvironmentVariable("LIBVA_DRIVER_NAME"));
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

        Assert.Throws<ArgumentConstructionException>(() => source.Build(type, Node, softwareOnly));
    }

    /// <summary>v4l2m2m is encoder-only upstream: empty input args are correct and the hardware encoder is still chosen.</summary>
    [Fact]
    public void V4l2m2mEmptyIsNotAnError()
    {
        var args = Build(HwType.v4l2m2m, null, new ProbeCell("h264", 8, "h264", HardwareDecode: false, HardwareEncode: true));

        Assert.Empty(args.InputArgs);
        Assert.Equal("h264_v4l2m2m", args.VideoEncoder);
        Assert.Null(args.HardwareDecoder);
        Assert.True(args.HardwareEncoder);
    }

    /// <summary>A build with no hardware encoder for the backend reports a software encoder.</summary>
    [Fact(Skip = "Requires macOS: EncodingHelper only emits VideoToolbox args there.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    public void MissingHardwareEncoderIsSoftware()
    {
        var caps = TestCapabilities.Full with { Encoders = new HashSet<string>(TestCapabilities.Full.Encoders.Where(e => e != "h264_videotoolbox")) };

        var args = new ArgumentSource(caps, _recorder).Build(HwType.videotoolbox, null, _smoke);

        Assert.False(args.HardwareEncoder);
    }

    /// <summary>Generation reaches only modelled IMediaEncoder members; the other five dependencies are never touched.</summary>
    [Fact]
    public void OnlyModelledMembersAreReached()
    {
        var source = new ArgumentSource(TestCapabilities.Full, _recorder);
        foreach (var type in Enum.GetValues<HwType>().Where(t => t != HwType.none))
        {
            foreach (var cell in new[] { _smoke, _hdr10 })
            {
                try
                {
                    source.Build(type, Node, cell);
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
    private ProbeArguments Build(HwType type, string? device, ProbeCell cell)
    {
        var args = new ArgumentSource(TestCapabilities.Full, _recorder).Build(type, device, cell);
        Assert.Empty(_recorder.Unexpected);
        return args;
    }
}
