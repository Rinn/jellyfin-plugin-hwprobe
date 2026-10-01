using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Ffmpeg;

/// <summary>Build enumeration against a real ffmpeg.</summary>
[Trait("Category", "RealFfmpeg")]
public sealed class FfmpegCapabilityProbeRealTests
{
    /// <summary>A real ffmpeg validates and lists encoders.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact(Skip = "Requires HWPROBE_HW_TESTS=1 and an ffmpeg (HWPROBE_TEST_FFMPEG or discovery).", SkipUnless = nameof(TestEnvironment.RealFfmpegAvailable), SkipType = typeof(TestEnvironment))]
    public async Task RealFfmpegEnumerates()
    {
        var caps = await ProbeAsync();

        Assert.Equal(FfmpegValidation.Valid, caps.Validation);
        Assert.NotEmpty(caps.Encoders);
    }

    /// <summary>On macOS the build reports VideoToolbox as selectable.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact(Skip = "Requires macOS, HWPROBE_HW_TESTS=1 and an ffmpeg.", SkipUnless = nameof(TestEnvironment.RealFfmpegOnMacOS), SkipType = typeof(TestEnvironment))]
    public async Task MacOSBuildHasVideoToolbox()
    {
        var caps = await ProbeAsync();

        Assert.Equal(BuildStatus.Selectable, caps.BuildStatus[HwType.videotoolbox]);
    }

    /// <summary>Runs build enumeration against the real ffmpeg.</summary>
    /// <returns>The capabilities.</returns>
    private static Task<FfmpegCapabilities> ProbeAsync() =>
        new FfmpegCapabilityProbe(new FfmpegRunner(), TimeSpan.FromSeconds(15))
            .ProbeAsync(TestEnvironment.RealFfmpeg!, TestContext.Current.CancellationToken);
}
