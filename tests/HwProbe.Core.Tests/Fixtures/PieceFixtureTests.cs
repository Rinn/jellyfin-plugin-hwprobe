using System.Security.Cryptography;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>Pinned pieces of large files: two range downloads, a hash check, then the encode reads the piece. Nothing is fetched from the network.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class PieceFixtureTests : IDisposable
{
    private static readonly byte[] _file = [.. Enumerable.Range(0, 1000).Select(i => (byte)i)];

    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-piece-").FullName;
    private readonly EncodingRunner _runner = new();

    /// <summary>The header and clusters are fetched, checked, read by the encode as <c>{piece}</c>, then deleted; the encode is cached.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MatchingPieceIsEncoded()
    {
        var downloader = new ScriptedDownloader(_file);
        var spec = Spec(Convert.ToHexStringLower(SHA256.HashData([.. _file[..10], .. _file[500..600]])));

        var result = await BuildAsync(spec, downloader);

        Assert.Equal(FixtureStatus.Available, result.Status);
        Assert.Equal(2, downloader.Calls);
        var input = _runner.Invocations.Single().Arguments;
        Assert.Contains(".piece.webm\" -t 30", input, StringComparison.Ordinal);
        Assert.False(File.Exists(result.Path + ".piece.webm"));

        await BuildAsync(spec, downloader);
        Assert.Equal(2, downloader.Calls);
    }

    /// <summary>A piece with no encode is the clip itself, cached with its manifest; nothing is run.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PieceWithoutEncodeIsTheClip()
    {
        var downloader = new ScriptedDownloader(_file);
        var spec = Spec(Convert.ToHexStringLower(SHA256.HashData([.. _file[..10], .. _file[500..600]]))) with { FileName = "sample.webm", EncodeArguments = string.Empty, RequiredEncoder = null };

        var result = await BuildAsync(spec, downloader);

        Assert.Equal(FixtureStatus.Available, result.Status);
        var saved = await File.ReadAllBytesAsync(result.Path!, TestContext.Current.CancellationToken);
        Assert.Equal(_file[..10].Concat(_file[500..600]), saved);
        Assert.Empty(_runner.Invocations);
        await BuildAsync(spec, downloader);
        Assert.Equal(2, downloader.Calls);
    }

    /// <summary>A clip that copies another reads it from the same cache directory, as <c>{clip:name}</c>.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ClipReadsAnEarlierClip()
    {
        FixtureSpec audio = new("audio.mka", "aac", 8, false, "aac", "-y -f lavfi -i sine -c:a aac", null);
        FixtureSpec video = new("video.mkv", "h264", 8, false, "libx264", "-y -f lavfi -i testsrc2 -i {clip:audio.mka} -c:a copy", null);

        var results = await new FixtureBuilder(_runner, "/fake/ffmpeg", _root, FixtureBuilder.DefaultTimeout, new ScriptedDownloader(_file), [audio, video], null)
            .BuildAsync("key", new HashSet<string> { "libx264", "aac" }, TestContext.Current.CancellationToken);

        Assert.All(results, r => Assert.Equal(FixtureStatus.Available, r.Status));
        Assert.Contains($"-i \"{Path.GetDirectoryName(results[0].Path)}{Path.DirectorySeparatorChar}audio.mka\" -c:a copy", _runner.Invocations[1].Arguments, StringComparison.Ordinal);
    }

    /// <summary>A piece whose bytes changed isn't encoded.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ChangedPieceIsRefused()
    {
        var result = await BuildAsync(Spec(new string('0', 64)), new ScriptedDownloader(_file));

        Assert.Equal(FixtureStatus.Untested, result.Status);
        Assert.Contains("not the pinned", result.Reason, StringComparison.Ordinal);
        Assert.Empty(_runner.Invocations);
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A fixture made from a piece of <see cref="_file"/>: 10 header bytes and 100 from offset 500.</summary>
    /// <param name="sha256">The pinned hash.</param>
    /// <returns>The fixture.</returns>
    private static FixtureSpec Spec(string sha256) =>
        new("sample.mkv", "h264", 8, false, "libx264", "-y -i {piece} -t 30 -c:v libx264", null)
        {
            Piece = new FixturePiece(new Uri("https://example.invalid/film.webm"), 10, 500, 100, sha256),
        };

    /// <summary>Builds one fixture.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="downloader">The downloader.</param>
    /// <returns>Its result.</returns>
    private async Task<FixtureResult> BuildAsync(FixtureSpec spec, ScriptedDownloader downloader) =>
        (await new FixtureBuilder(_runner, "/fake/ffmpeg", _root, FixtureBuilder.DefaultTimeout, downloader, [spec], null)
            .BuildAsync("key", new HashSet<string> { "libx264" }, TestContext.Current.CancellationToken)).Single();
}
