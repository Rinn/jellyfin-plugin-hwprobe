using System.Security.Cryptography;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Tests.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>Fixtures that are downloaded and pinned by hash rather than generated.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class DownloadedFixtureTests : IDisposable
{
    private static readonly byte[] _sample = [1, 2, 3, 4];

    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-download-").FullName;

    /// <summary>A sample matching its pinned hash is cached and not downloaded again, by the same cache key or another (a different ffmpeg build).</summary>
    /// <param name="secondKey">The cache key of the second build.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("ffmpeg-a")]
    [InlineData("ffmpeg-b")]
    public async Task MatchingSampleIsDownloadedOnce(string secondKey)
    {
        var downloader = new ScriptedDownloader(_sample);

        var first = await BuildAsync(downloader, Spec(Hash(_sample)), "ffmpeg-a");
        var second = await BuildAsync(downloader, Spec(Hash(_sample)), secondKey);

        Assert.Equal((FixtureStatus.Available, FixtureStatus.Available), (first.Status, second.Status));
        Assert.Equal(first.Path, second.Path);
        Assert.Equal(1, downloader.Calls);
    }

    /// <summary>A sample that doesn't match the pinned hash, or can't be downloaded, is Untested with the reason and not cached.</summary>
    /// <param name="offline">Whether the host is offline, rather than served a different file.</param>
    /// <param name="reason">Text the reason contains.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false, "not the pinned")]
    [InlineData(true, "network is unreachable")]
    public async Task UnverifiedSampleIsUntested(bool offline, string reason)
    {
        var result = await BuildAsync(offline ? ScriptedDownloader.Offline : new ScriptedDownloader(_sample), Spec(offline ? Hash(_sample) : new string('0', 64)));

        Assert.Equal(FixtureStatus.Untested, result.Status);
        Assert.Contains(reason, result.Reason, StringComparison.Ordinal);
        Assert.Null(result.Path);
    }

    /// <summary>A clip that fails to generate falls back to its pinned sample, cached with the URL's extension.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FailedGenerationFallsBackToDownload()
    {
        var downloader = new ScriptedDownloader(_sample);

        var result = await BuildAsync(new EncodingRunner { ExitCode = 1 }, downloader, Generated(Hash(_sample)), ["libx264"]);

        Assert.Equal(FixtureStatus.Available, result.Status);
        Assert.EndsWith(".jsv", result.Path, StringComparison.Ordinal);
        Assert.Equal(1, downloader.Calls);
    }

    /// <summary>A build without the encoder falls back to the pinned sample.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MissingEncoderFallsBackToDownload()
    {
        var runner = new EncodingRunner();

        var result = await BuildAsync(runner, new ScriptedDownloader(_sample), Generated(Hash(_sample)), []);

        Assert.Equal(FixtureStatus.Available, result.Status);
        Assert.Empty(runner.Invocations);
    }

    /// <summary>When the download fails too, the generation failure stands with both reasons.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FailedFallbackKeepsBothReasons()
    {
        var result = await BuildAsync(new EncodingRunner { ExitCode = 1 }, ScriptedDownloader.Offline, Generated(Hash(_sample)), ["libx264"]);

        Assert.Equal(FixtureStatus.Failed, result.Status);
        Assert.Contains("Unknown encoder", result.Reason, StringComparison.Ordinal);
        Assert.Contains("; download: could not download", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>A clip that generates never downloads its sample.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GeneratedClipIsNotDownloaded()
    {
        var downloader = new ScriptedDownloader(_sample);

        var result = await BuildAsync(new EncodingRunner(), downloader, Generated(Hash(_sample)), ["libx264"]);

        Assert.Equal(FixtureStatus.Available, result.Status);
        Assert.Equal(0, downloader.Calls);
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Lowercase hex SHA-256.</summary>
    /// <param name="bytes">The data.</param>
    /// <returns>The hash.</returns>
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>A downloadable fixture spec.</summary>
    /// <param name="sha256">The pinned hash.</param>
    /// <returns>The spec.</returns>
    private static FixtureSpec Spec(string sha256) =>
        new("sample.vc1", "vc1", 8, false, null, string.Empty, null) { DownloadUrl = new Uri("https://example.invalid/sample.vc1"), Sha256 = sha256 };

    /// <summary>A generated fixture spec with a pinned fallback sample.</summary>
    /// <param name="sha256">The pinned hash.</param>
    /// <returns>The spec.</returns>
    private static FixtureSpec Generated(string sha256) =>
        new("sample.mp4", "h264", 8, false, "libx264", "-c:v libx264", null) { DownloadUrl = new Uri("https://example.invalid/sample.jsv"), Sha256 = sha256 };

    /// <summary>Builds a one-fixture catalog with a given runner and encoder set.</summary>
    /// <param name="runner">The ffmpeg runner.</param>
    /// <param name="downloader">The downloader.</param>
    /// <param name="spec">The fixture.</param>
    /// <param name="encoders">Encoders in the build.</param>
    /// <returns>Its result.</returns>
    private async Task<FixtureResult> BuildAsync(IFfmpegRunner runner, IFixtureDownloader downloader, FixtureSpec spec, HashSet<string> encoders)
    {
        var results = await new FixtureBuilder(runner, "/fake/ffmpeg", _root, TimeSpan.FromSeconds(5), downloader, [spec], null)
            .BuildAsync("key", encoders, TestContext.Current.CancellationToken);
        return Assert.Single(results);
    }

    /// <summary>Builds a one-fixture catalog.</summary>
    /// <param name="downloader">The downloader.</param>
    /// <param name="spec">The fixture.</param>
    /// <param name="cacheKey">The fixture cache key.</param>
    /// <returns>Its result.</returns>
    private async Task<FixtureResult> BuildAsync(IFixtureDownloader downloader, FixtureSpec spec, string cacheKey = "key")
    {
        var results = await new FixtureBuilder(new EngineRunner(), "/fake/ffmpeg", _root, TimeSpan.FromSeconds(5), downloader, [spec], null)
            .BuildAsync(cacheKey, new HashSet<string>(), TestContext.Current.CancellationToken);
        return Assert.Single(results);
    }
}
