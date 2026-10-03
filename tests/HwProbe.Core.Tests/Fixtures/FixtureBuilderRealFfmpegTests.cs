using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>Generates the real fixture set with the dev machine's ffmpeg.</summary>
[Trait("Category", "RealFfmpeg")]
public sealed class FixtureBuilderRealFfmpegTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-fixtures-real-").FullName;

    /// <summary>Every fixture whose encoder is in the build generates, and the rest are Skipped or Untested; nothing is downloaded.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact(Skip = "Requires HWPROBE_HW_TESTS=1 and an ffmpeg (HWPROBE_TEST_FFMPEG or discovery).", SkipUnless = nameof(TestEnvironment.RealFfmpegAvailable), SkipType = typeof(TestEnvironment))]
    public async Task GeneratesWithRealFfmpeg()
    {
        var ffmpeg = TestEnvironment.RealFfmpeg!;
        var ct = TestContext.Current.CancellationToken;
        var runner = new FfmpegRunner();
        var encoders = (await new FfmpegCapabilityProbe(runner, TimeSpan.FromSeconds(15)).ProbeAsync(ffmpeg, ct)).Encoders;

        var results = await new FixtureBuilder(runner, ffmpeg, _root, FixtureBuilder.DefaultTimeout, ScriptedDownloader.Offline, FixtureCatalog.All, null)
            .BuildAsync("real", encoders, ct);

        Assert.All(results, r => Assert.True(r.Status != FixtureStatus.Failed, $"{r.Spec.FileName}: {r.Reason}"));
        Assert.Equal(FixtureStatus.Available, results.Single(r => r.Spec.FileName == "h264_8bit.mp4").Status);
        Assert.Equal(FixtureStatus.Untested, results.Single(r => r.Spec.Codec == "vc1").Status);
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
