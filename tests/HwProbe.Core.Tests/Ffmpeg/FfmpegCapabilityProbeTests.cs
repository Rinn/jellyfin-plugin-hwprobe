using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Ffmpeg;

/// <summary>Build enumeration end to end over scripted ffmpeg output.</summary>
[Trait("Category", "Unit")]
public sealed class FfmpegCapabilityProbeTests
{
    private const string OverlayEofHelpText = "Action to take when encountering EOF from secondary input";

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    /// <summary>Homebrew on macOS: only VideoToolbox is built, and it is not a Jellyfin build.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task HomebrewMacBuild()
    {
        var runner = ScriptedFfmpegRunner.FromCorpus("homebrew-9.0.2-macos");

        var caps = await new FfmpegCapabilityProbe(runner, _timeout).ProbeAsync("/opt/homebrew/bin/ffmpeg", TestContext.Current.CancellationToken);

        Assert.Equal(FfmpegValidation.Valid, caps.Validation);
        Assert.Equal(new Version(9, 0, 2), caps.Version);
        Assert.False(caps.IsJellyfinBuild);
        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.videotoolbox]);
        Assert.All(
            caps.BuildStatus.Where(s => s.Key != HwType.videotoolbox),
            s => Assert.Equal(BuildStatus.NotBuilt, s.Value));
        Assert.All(caps.FilterOptions.Values, Assert.False);
        Assert.False(caps.IsOpenclFullSupported);
    }

    /// <summary>A cuda-built ffmpeg reports nvenc Selectable regardless of hardware — the gap this tool exists for.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CudaBuildIsSelectableWithoutDevice()
    {
        var caps = await ProbeAsync(FullBuild());

        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.nvenc]);
        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.vaapi]);
        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.qsv]);
        Assert.Equal(BuildStatus.NotBuilt, caps.BuildStatus[HwType.rkmpp]);
    }

    /// <summary>OpenCL-full needs the hwaccel, scale_opencl and both filter options.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OpenclFullWhenAllGatesPass()
    {
        var caps = await ProbeAsync(FullBuild());

        Assert.True(caps.FilterOptions["TonemapOpenclBt2390"]);
        Assert.True(caps.FilterOptions["OverlayOpenclFrameSync"]);
        Assert.True(caps.IsOpenclFullSupported);
    }

    /// <summary>A tonemap_opencl without bt2390 drops OpenCL-full.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OpenclNotFullWithoutBt2390()
    {
        var script = FullBuild();
        script["-h filter=tonemap_opencl"] = "Filter tonemap_opencl\n  tonemap: hable reinhard\n";

        var caps = await ProbeAsync(script);

        Assert.False(caps.FilterOptions["TonemapOpenclBt2390"]);
        Assert.False(caps.IsOpenclFullSupported);
    }

    /// <summary>Each filter's help is fetched once, and absent filters are never asked about.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task HelpFetchedOncePerPresentFilter()
    {
        var runner = new ScriptedFfmpegRunner(FullBuild());

        await new FfmpegCapabilityProbe(runner, _timeout).ProbeAsync("/ffmpeg", TestContext.Current.CancellationToken);

        var helpCalls = runner.Calls.Where(c => c.StartsWith("-h filter=", StringComparison.Ordinal)).ToList();
        Assert.Equal(["-h filter=tonemap_opencl", "-h filter=overlay_opencl"], helpCalls);
    }

    /// <summary>amf needs an amf encoder, not just d3d11va.</summary>
    /// <param name="encoderRow">An extra encoder row, or empty.</param>
    /// <param name="expected">The expected amf status.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("", BuildStatus.NotBuilt)]
    [InlineData(" V....D h264_amf             AMD AMF H.264 Encoder (codec h264)\n", BuildStatus.Selectable)]
    public async Task AmfNeedsEncoder(string encoderRow, BuildStatus expected)
    {
        var script = FullBuild();
        script["-hwaccels"] = "Hardware acceleration methods:\nd3d11va\n";
        script["-encoders"] += encoderRow;

        var caps = await ProbeAsync(script);

        Assert.Equal(expected, caps.BuildStatus[HwType.amf]);
    }

    /// <summary>v4l2m2m is built when its encoder is, with no hwaccel involved.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task V4l2m2mFollowsEncoder()
    {
        var script = FullBuild();
        script["-encoders"] += " V..... h264_v4l2m2m         V4L2 mem2mem H.264 encoder wrapper (codec h264)\n";

        var caps = await ProbeAsync(script);

        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.v4l2m2m]);
    }

    /// <summary>The lookups used by tier gates answer by upstream names, including FilterOptionType keys.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LookupsAnswerTierGateQueries()
    {
        var script = FullBuild();
        script["-filters"] += " ... overlay_vaapi     VV->V      Overlay one video on top of another\n";
        script["-h filter=overlay_vaapi"] = $"Filter overlay_vaapi\n  eof_action <int> {OverlayEofHelpText}\n";

        var caps = await ProbeAsync(script);
        Func<string, bool> hwaccel = caps.SupportsHwaccel;
        Func<string, bool> filter = caps.SupportsFilter;
        Func<string, bool> option = caps.SupportsFilterWithOption;

        Assert.True(hwaccel("drm"));
        Assert.False(hwaccel("rkmpp"));
        Assert.True(filter("alphasrc"));
        Assert.True(option("OverlayVaapiFrameSync"));
        Assert.False(option("OverlayVulkanFrameSync"));
        Assert.False(option("NotAnUpstreamKey"));
    }

    /// <summary>A failed <c>-version</c> launch yields NoOutput rather than throwing.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MissingVersionIsNoOutput()
    {
        var caps = await ProbeAsync([]);

        Assert.Equal(FfmpegValidation.NoOutput, caps.Validation);
    }

    /// <summary>Probes a scripted build.</summary>
    /// <param name="script">Stdout per argument string.</param>
    /// <returns>The capabilities.</returns>
    private static Task<FfmpegCapabilities> ProbeAsync(Dictionary<string, string> script) =>
        new FfmpegCapabilityProbe(new ScriptedFfmpegRunner(script), _timeout).ProbeAsync("/ffmpeg", TestContext.Current.CancellationToken);

    /// <summary>A synthetic jellyfin-ffmpeg-like Linux build with cuda, vaapi, qsv and OpenCL.</summary>
    /// <returns>Stdout per argument string.</returns>
    /// <remarks>Synthesized, not recorded: no jellyfin-ffmpeg binary was available.</remarks>
    private static Dictionary<string, string> FullBuild() => new(StringComparer.Ordinal)
    {
        ["-version"] = "ffmpeg version 7.1.1-Jellyfin Copyright (c) 2000-2025 the FFmpeg developers\n",
        ["-hwaccels"] = "Hardware acceleration methods:\ncuda\nvaapi\nqsv\ndrm\nopencl\nvulkan\n",
        ["-encoders"] = "Encoders:\n ------\n V....D h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)\n V....D h264_vaapi           H.264/AVC (VAAPI) (codec h264)\n",
        ["-decoders"] = "Decoders:\n ------\n V....D h264                 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10\n",
        ["-filters"] = "Filters:\n  ------\n ... scale_opencl      V->V       Scale the input video size through OpenCL.\n ... tonemap_opencl    V->V       Perform HDR to SDR conversion with tonemapping.\n ... overlay_opencl    VV->V      Overlay one video on top of another\n ... alphasrc          |->V       Generate a video with alpha.\n",
        ["-h filter=tonemap_opencl"] = "Filter tonemap_opencl\n  tonemap: none linear gamma clip reinhard hable mobius bt2390\n",
        ["-h filter=overlay_opencl"] = $"Filter overlay_opencl\n  eof_action <int> {OverlayEofHelpText}\n  alpha_format <int>\n",
    };
}
