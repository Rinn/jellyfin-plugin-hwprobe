using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>Caching, skipping and corruption handling of <see cref="FixtureBuilder"/>.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class FixtureBuilderTests : IDisposable
{
    private const string Key = "sha256:abc";

    private static readonly HashSet<string> _allEncoders =
        [.. FixtureCatalog.All.Select(f => f.RequiredEncoder).OfType<string>()];

    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-fixtures-").FullName;
    private readonly EncodingRunner _runner = new();

    /// <summary>With every encoder present, all generatable fixtures become available.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GeneratesEveryBuildableFixture()
    {
        var results = await BuildAsync(_allEncoders);

        Assert.Equal(FixtureCatalog.All.Count, results.Count);
        Assert.All(results.Where(r => r.Spec.UntestedReason is null && r.Spec.RequiredEncoder is not null), r =>
        {
            Assert.Equal(FixtureStatus.Available, r.Status);
            Assert.True(File.Exists(r.Path));
        });
        Assert.Equal(FixtureCatalog.All.Count - 1, _runner.Invocations.Count);
    }

    /// <summary>Offline, the downloaded VC-1 sample is reported Untested with a reason, never omitted.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Vc1OfflineIsUntestedNotOmitted()
    {
        var results = await BuildAsync(_allEncoders);

        var vc1 = Assert.Single(results, r => r.Spec.Codec == "vc1");
        Assert.Equal(FixtureStatus.Untested, vc1.Status);
        Assert.False(string.IsNullOrEmpty(vc1.Reason));
    }

    /// <summary>A missing software encoder skips its fixtures without launching ffmpeg for them.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MissingEncoderIsSkippedNotFailed()
    {
        var withoutX265 = _allEncoders.Where(e => e != "libx265").ToHashSet();

        var results = await BuildAsync(withoutX265);

        var hevc = results.Where(r => r.Spec.RequiredEncoder == "libx265").ToList();
        Assert.Equal(FixtureCatalog.All.Count(f => f.RequiredEncoder == "libx265"), hevc.Count);
        Assert.All(hevc.Where(r => !r.Spec.Bundled), r => Assert.Equal(FixtureStatus.Skipped, r.Status));
        Assert.All(hevc.Where(r => r.Spec.Bundled), r => Assert.Equal(FixtureStatus.Available, r.Status));
        Assert.DoesNotContain(_runner.Invocations, i => i.Arguments.Contains("libx265", StringComparison.Ordinal));
    }

    /// <summary>A second build with an intact cache launches nothing.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SecondRunIsCacheHit()
    {
        await BuildAsync(_allEncoders);
        _runner.Invocations.Clear();

        var results = await BuildAsync(_allEncoders);

        Assert.Empty(_runner.Invocations);
        Assert.All(results.Where(r => r.Spec.UntestedReason is null && r.Spec.RequiredEncoder is not null), r => Assert.Equal(FixtureStatus.Available, r.Status));
    }

    /// <summary>A damaged cache entry is regenerated, and only that fixture: a truncated clip fails its manifest check, a manifest from different encode arguments (an older catalog) is stale, and a missing manifest (a run killed mid-way) is incomplete.</summary>
    /// <param name="fileName">The fixture damaged.</param>
    /// <param name="damage">How it is damaged: <c>truncate</c>, <c>recipe</c> or <c>manifest</c>.</param>
    /// <param name="argument">Text in that fixture's encode arguments.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("h264_8bit.mp4", "truncate", "libx264")]
    [InlineData("hevc_10bit.mp4", "recipe", "yuv420p10le")]
    [InlineData("mpeg2.mpg", "manifest", "mpeg2video")]
    public async Task DamagedFixtureIsRegenerated(string fileName, string damage, string argument)
    {
        var first = await BuildAsync(_allEncoders);
        var path = first.Single(r => r.Spec.FileName == fileName).Path!;
        var manifest = path + ".sha256";
        var token = TestContext.Current.CancellationToken;
        switch (damage)
        {
            case "truncate":
                await File.WriteAllTextAsync(path, "trunc", token);
                break;
            case "recipe":
                var fields = (await File.ReadAllTextAsync(manifest, token)).Split(' ');
                await File.WriteAllTextAsync(manifest, $"{fields[0]} {fields[1]} {new string('0', 64)}", token);
                break;
            default:
                File.Delete(manifest);
                break;
        }

        _runner.Invocations.Clear();

        var second = await BuildAsync(_allEncoders);

        Assert.Contains(argument, Assert.Single(_runner.Invocations).Arguments, StringComparison.Ordinal);
        Assert.Equal(FixtureStatus.Available, second.Single(r => r.Spec.FileName == fileName).Status);
    }

    /// <summary>A failed encode is reported and leaves nothing cached, so the next run retries.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FailedEncodeIsNotCached()
    {
        _runner.ExitCode = 1;
        var failed = await BuildAsync(_allEncoders);

        var h264 = failed.Single(r => r.Spec.FileName == "h264_8bit.mp4");
        Assert.Equal(FixtureStatus.Failed, h264.Status);
        Assert.Contains("Unknown encoder", h264.Reason, StringComparison.Ordinal);

        // Only bundled copies, which don't depend on the encode, are written.
        var downloads = Path.DirectorySeparatorChar + "downloads" + Path.DirectorySeparatorChar;
        Assert.DoesNotContain(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories), f => !f.Contains(downloads, StringComparison.Ordinal));
        Assert.All(failed.Where(r => r.Spec.Bundled), r => Assert.Equal(FixtureStatus.Available, r.Status));

        _runner.ExitCode = 0;
        _runner.Invocations.Clear();
        await BuildAsync(_allEncoders);
        Assert.Equal(FixtureCatalog.All.Count - 1, _runner.Invocations.Count);
    }

    /// <summary>An AV1 encode that crashes is retried with SVT-AV1's C code, and the clip is made.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CrashedAv1EncodeIsRetriedWithCCode()
    {
        _runner.Crashes = i => i.Arguments.Contains("libsvtav1", StringComparison.Ordinal) && !i.Arguments.Contains("asm=c", StringComparison.Ordinal);

        var results = await BuildAsync(_allEncoders);

        Assert.All(results.Where(r => r.Spec.Codec == "av1"), r => Assert.Equal(FixtureStatus.Available, r.Status));
        Assert.Equal(2 * FixtureCatalog.All.Count(f => f.RequiredEncoder == "libsvtav1"), _runner.Invocations.Count(i => i.Arguments.Contains("libsvtav1", StringComparison.Ordinal)));
    }

    /// <summary>When the retry also fails, both reasons are reported and nothing is cached.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FailedRetryReportsBothReasons()
    {
        _runner.Crashes = i => i.Arguments.Contains("libsvtav1", StringComparison.Ordinal);

        var results = await BuildAsync(_allEncoders);

        var av1 = results.Single(r => r.Spec.FileName == "av1_8bit.mp4");
        Assert.Equal(FixtureStatus.Failed, av1.Status);
        Assert.Contains("retry: ffmpeg exited 139", av1.Reason, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(_root, "av1_8bit*", SearchOption.AllDirectories));
    }

    /// <summary>Encodes write to a temp name with the real extension, never the final path.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task EncodesToPartialName()
    {
        await BuildAsync(_allEncoders);

        Assert.All(_runner.Invocations, i =>
        {
            var output = EncodingRunner.OutputPath(i);
            Assert.Contains(".partial.", Path.GetFileName(output), StringComparison.Ordinal);
        });
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Runs a build against the temp cache.</summary>
    /// <param name="encoders">Encoders the fake build has.</param>
    /// <returns>The fixture results.</returns>
    private Task<IReadOnlyList<FixtureResult>> BuildAsync(IReadOnlySet<string> encoders) =>
        new FixtureBuilder(_runner, "/fake/ffmpeg", _root, FixtureBuilder.DefaultTimeout, ScriptedDownloader.Offline)
            .BuildAsync(Key, encoders, TestContext.Current.CancellationToken);
}
