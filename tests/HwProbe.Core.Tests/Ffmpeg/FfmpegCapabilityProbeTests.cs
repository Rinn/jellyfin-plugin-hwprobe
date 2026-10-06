using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Ffmpeg;

/// <summary>Build enumeration end to end over scripted ffmpeg output.</summary>
[Trait("Category", "Unit")]
public sealed class FfmpegCapabilityProbeTests
{
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

    /// <summary>jellyfin-ffmpeg on Linux: cuda, vaapi, qsv and v4l2m2m are Selectable regardless of hardware (the gap this tool exists for), amf is not without d3d11va, and OpenCL is full.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task JellyfinLinuxBuild()
    {
        var caps = await ProbeAsync(Corpus());

        Assert.Equal(FfmpegValidation.Valid, caps.Validation);
        Assert.True(caps.IsJellyfinBuild);
        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.nvenc]);
        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.vaapi]);
        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.qsv]);
        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.v4l2m2m]);
        Assert.Equal(BuildStatus.NotBuilt, caps.BuildStatus[HwType.amf]);
        Assert.Equal(BuildStatus.NotBuilt, caps.BuildStatus[HwType.rkmpp]);
        Assert.True(caps.FilterOptions["TonemapOpenclBt2390"]);
        Assert.True(caps.FilterOptions["OverlayOpenclFrameSync"]);
        Assert.True(caps.IsOpenclFullSupported);
    }

    /// <summary>A tonemap_opencl without bt2390 drops OpenCL-full.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OpenclNotFullWithoutBt2390()
    {
        var script = Corpus();
        script["-h filter=tonemap_opencl"] = script["-h filter=tonemap_opencl"].Replace("bt2390", string.Empty, StringComparison.Ordinal);

        var caps = await ProbeAsync(script);

        Assert.False(caps.FilterOptions["TonemapOpenclBt2390"]);
        Assert.False(caps.IsOpenclFullSupported);
    }

    /// <summary>Each filter's help is fetched once, and absent filters are never asked about.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task HelpFetchedOncePerPresentFilter()
    {
        var script = Corpus();
        script["-filters"] = WithoutLines(script["-filters"], " overlay_vulkan ");
        var runner = new ScriptedFfmpegRunner(script);

        await new FfmpegCapabilityProbe(runner, _timeout).ProbeAsync("/ffmpeg", TestContext.Current.CancellationToken);

        var helpCalls = runner.Calls.Where(c => c.StartsWith("-h filter=", StringComparison.Ordinal)).ToList();
        Assert.Equal(["-h filter=scale_cuda", "-h filter=tonemap_cuda", "-h filter=tonemap_opencl", "-h filter=overlay_opencl", "-h filter=overlay_vaapi", "-h filter=transpose_opencl", "-h filter=overlay_cuda"], helpCalls);
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
        var script = Corpus();
        script["-hwaccels"] = "Hardware acceleration methods:\nd3d11va\n";
        script["-encoders"] = WithoutLines(script["-encoders"], "_amf ") + encoderRow;

        var caps = await ProbeAsync(script);

        Assert.Equal(expected, caps.BuildStatus[HwType.amf]);
    }

    /// <summary>v4l2m2m is built when its H.264 encoder is, with no hwaccel involved.</summary>
    /// <param name="encoderRow">An extra encoder row, or empty.</param>
    /// <param name="expected">The expected v4l2m2m status.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("", BuildStatus.NotBuilt)]
    [InlineData(" V..... h264_v4l2m2m         V4L2 mem2mem H.264 encoder wrapper (codec h264)\n", BuildStatus.Selectable)]
    public async Task V4l2m2mFollowsEncoder(string encoderRow, BuildStatus expected)
    {
        var script = Corpus();
        script["-encoders"] = WithoutLines(script["-encoders"], "_v4l2m2m ") + encoderRow;

        var caps = await ProbeAsync(script);

        Assert.Equal(expected, caps.BuildStatus[HwType.v4l2m2m]);
    }

    /// <summary>The lookups used by tier gates answer by upstream names, including FilterOptionType keys.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LookupsAnswerTierGateQueries()
    {
        var script = Corpus();
        script["-filters"] = WithoutLines(script["-filters"], " overlay_vulkan ");

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

    /// <summary>Reads the recorded jellyfin-ffmpeg 8.1.2 Linux amd64 build.</summary>
    /// <returns>Stdout per argument string.</returns>
    private static Dictionary<string, string> Corpus() => ScriptedFfmpegRunner.LoadCorpus("jellyfin-8.1.2-linux-amd64");

    /// <summary>Drops the lines containing a substring.</summary>
    /// <param name="text">The recorded output.</param>
    /// <param name="contains">The substring.</param>
    /// <returns>The output without those lines.</returns>
    private static string WithoutLines(string text, string contains) =>
        string.Join('\n', text.Split('\n').Where(l => !l.Contains(contains, StringComparison.Ordinal)));
}
