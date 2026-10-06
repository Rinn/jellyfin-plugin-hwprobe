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
    private readonly CallRecorder _recorder = new();

    /// <summary>HwType values and names match upstream's HardwareAccelerationType exactly.</summary>
    [Fact]
    public void HwTypeMirrorsUpstreamEnum()
    {
        var ours = Enum.GetValues<HwType>().Select(v => (Name: v.ToString(), Value: (int)v));
        var upstream = Enum.GetValues<HardwareAccelerationType>().Select(v => (Name: v.ToString(), Value: (int)v));

        Assert.Equal(upstream, ours);
    }

    /// <summary>A low-power probe cell is built at a real bitrate, from a probe clip without audio, as a speed cell is.</summary>
    [Fact]
    public void FullQualityProbeCellBuilds()
    {
        var cell = ArgumentSourceCells.Smoke with { FullQuality = true, VideoBitrate = 8_000_000 };

        var args = Build(HwType.none, null, cell);

        Assert.Contains("8000000", args.EncoderArgs, StringComparison.Ordinal);
    }

    /// <summary>VAAPI on Linux: device init names the render node and the decoder outputs VAAPI surfaces.</summary>
    [Fact(Skip = "Requires Linux: EncodingHelper only emits VAAPI args there.", SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void VaapiSmoke()
    {
        var args = Build(HwType.vaapi, ArgumentSourceCells.Node, ArgumentSourceCells.Smoke);

        Assert.StartsWith("-init_hw_device vaapi=va", args.InputArgs, StringComparison.Ordinal);
        Assert.Contains("-hwaccel vaapi -hwaccel_output_format vaapi -noautorotate", args.InputArgs, StringComparison.Ordinal);
        Assert.Equal("h264_vaapi", args.VideoEncoder);
    }

    /// <summary>QSV on Linux derives the QSV device from a VAAPI parent and never uses child_device.</summary>
    [Fact(Skip = "Requires Linux: EncodingHelper only emits QSV-over-VAAPI args there.", SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void QsvDerivesFromVaapi()
    {
        var args = Build(HwType.qsv, ArgumentSourceCells.Node, ArgumentSourceCells.Smoke);

        Assert.Contains("-init_hw_device qsv=qs@va", args.InputArgs, StringComparison.Ordinal);
        Assert.DoesNotContain("child_device", args.InputArgs, StringComparison.Ordinal);
        Assert.Equal("h264_qsv", args.VideoEncoder);
    }

    /// <summary>Upstream asks VAAPI for low-power only on Intel drivers, and only when the cell enables it.</summary>
    [Fact(Skip = "Requires Linux: EncodingHelper only emits VAAPI args there.", SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void VaapiLowPowerNeedsIntelDriver()
    {
        var lowPower = ArgumentSourceCells.Smoke with { LowPower = true };
        var amd = TestCapabilities.Full with { IsVaapiDeviceInteliHD = false, IsVaapiDeviceAmd = true };

        var intel = Build(HwType.vaapi, ArgumentSourceCells.Node, lowPower);

        Assert.True(intel.LowPowerEncoder);
        Assert.Equal(" -low_power 1", intel.EncoderArgs);
        Assert.False(Build(HwType.vaapi, ArgumentSourceCells.Node, ArgumentSourceCells.Smoke).LowPowerEncoder);
        Assert.False(new ArgumentSource(amd, _recorder).Build(HwType.vaapi, ArgumentSourceCells.Node, lowPower).LowPowerEncoder);
    }

    /// <summary>i965 sets two env vars during generation; they are returned for the child and the parent is restored.</summary>
    [Fact(Skip = "Requires Linux: the i965 branch only runs there.", SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void I965EnvironmentIsReturnedAndRestored()
    {
        var before = Environment.GetEnvironmentVariable("LIBVA_DRIVER_NAME");
        var caps = TestCapabilities.Full with { IsVaapiDeviceInteliHD = false, IsVaapiDeviceInteli965 = true };

        var args = new ArgumentSource(caps, _recorder).Build(HwType.vaapi, ArgumentSourceCells.Node, ArgumentSourceCells.Smoke);

        Assert.Equal("i965", args.Environment["LIBVA_DRIVER_NAME"]);
        Assert.Equal("i965", args.Environment["LIBVA_DRIVER_NAME_JELLYFIN"]);
        Assert.Equal(before, Environment.GetEnvironmentVariable("LIBVA_DRIVER_NAME"));
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

    /// <summary>Builds arguments with the full capability set, asserting no unmodelled member was reached.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device, or null.</param>
    /// <param name="cell">The media shape.</param>
    /// <returns>The generated arguments.</returns>
    private ProbeArguments Build(HwType type, string? device, ProbeCell cell) => ArgumentSourceCells.Build(_recorder, type, device, cell);
}
