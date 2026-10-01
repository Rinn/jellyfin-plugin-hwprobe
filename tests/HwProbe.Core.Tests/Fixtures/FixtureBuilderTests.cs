using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>Caching, skipping and corruption handling of <see cref="FixtureBuilder"/>.</summary>
[Trait("Category", "Unit")]
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
        Assert.All(results.Where(r => r.Spec.UntestedReason is null), r =>
        {
            Assert.Equal(FixtureStatus.Available, r.Status);
            Assert.True(File.Exists(r.Path));
        });
        Assert.Equal(FixtureCatalog.All.Count - 1, _runner.Invocations.Count);
    }

    /// <summary>VC-1 is reported Untested with a reason, never omitted.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Vc1IsUntestedNotOmitted()
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
        Assert.Equal(5, hevc.Count);
        Assert.All(hevc, r => Assert.Equal(FixtureStatus.Skipped, r.Status));
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
        Assert.All(results.Where(r => r.Spec.UntestedReason is null), r => Assert.Equal(FixtureStatus.Available, r.Status));
    }

    /// <summary>A truncated cached fixture fails its manifest check and is regenerated.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CorruptFixtureIsRegenerated()
    {
        var first = await BuildAsync(_allEncoders);
        var h264 = first.Single(r => r.Spec.FileName == "h264_8bit.mp4").Path!;
        await File.WriteAllTextAsync(h264, "trunc", TestContext.Current.CancellationToken);
        _runner.Invocations.Clear();

        var second = await BuildAsync(_allEncoders);

        var invocation = Assert.Single(_runner.Invocations);
        Assert.Contains("libx264", invocation.Arguments, StringComparison.Ordinal);
        Assert.Equal(FixtureStatus.Available, second.Single(r => r.Spec.FileName == "h264_8bit.mp4").Status);
    }

    /// <summary>A fixture made from different encode arguments (an older catalog) is regenerated.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ChangedRecipeIsRegenerated()
    {
        var first = await BuildAsync(_allEncoders);
        var manifest = first.Single(r => r.Spec.FileName == "hevc_10bit.mp4").Path! + ".sha256";
        var fields = (await File.ReadAllTextAsync(manifest, TestContext.Current.CancellationToken)).Split(' ');
        await File.WriteAllTextAsync(manifest, $"{fields[0]} {fields[1]} {new string('0', 64)}", TestContext.Current.CancellationToken);
        _runner.Invocations.Clear();

        await BuildAsync(_allEncoders);

        Assert.Contains("yuv420p10le", Assert.Single(_runner.Invocations).Arguments, StringComparison.Ordinal);
    }

    /// <summary>A fixture with no manifest (e.g. a run killed mid-way) is regenerated.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MissingManifestIsRegenerated()
    {
        var first = await BuildAsync(_allEncoders);
        File.Delete(first.Single(r => r.Spec.FileName == "mpeg2.mpg").Path! + ".sha256");
        _runner.Invocations.Clear();

        await BuildAsync(_allEncoders);

        Assert.Contains("mpeg2video", Assert.Single(_runner.Invocations).Arguments, StringComparison.Ordinal);
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
        Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));

        _runner.ExitCode = 0;
        _runner.Invocations.Clear();
        await BuildAsync(_allEncoders);
        Assert.Equal(FixtureCatalog.All.Count - 1, _runner.Invocations.Count);
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
        new FixtureBuilder(_runner, "/fake/ffmpeg", _root, FixtureBuilder.DefaultTimeout)
            .BuildAsync(Key, encoders, TestContext.Current.CancellationToken);
}
