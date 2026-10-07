using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>Listing the fixture cache in <see cref="FixtureCacheContents"/>.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class FixtureCacheContentsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-cache-").FullName;

    /// <summary>A missing cache lists nothing.</summary>
    [Fact]
    public void MissingCacheIsEmpty() => Assert.Empty(FixtureCacheContents.List(Path.Combine(_root, "none")));

    /// <summary>Files are named from the catalogs, hash files fold into their clip, and unknown files have no description.</summary>
    [Fact]
    public void NamesFilesFromTheCatalogs()
    {
        var pgs = SpeedCatalog.ImageSubtitles;
        Write("sha256_abc", FixtureCatalog.Hevc10.FileName, 100);
        Write("sha256_abc", FixtureCatalog.Hevc10.FileName + ".sha256", 64);
        Write("sha256_abc", "old_clip.mp4", 10);
        Write("downloads", pgs.Sha256 + ".sup", 50);
        var musepack = SpeedCatalog.FindAudio("musepack");
        var flac = SpeedCatalog.FindAudio("flac");
        Assert.NotNull(musepack);
        Assert.NotNull(flac);
        Write("downloads", musepack.Fixture.Sha256 + ".mpc", 20);
        Write("sha256_abc", flac.Fixture.FileName, 30);

        var entries = FixtureCacheContents.List(_root);

        Assert.Equal(5, entries.Count);
        Assert.Equal("Audio, Musepack", Assert.Single(entries, e => e.File.EndsWith(".mpc", StringComparison.Ordinal)).Description);
        Assert.Equal("Audio, FLAC", Assert.Single(entries, e => e.File == "audio_flac.flac").Description);
        var clip = Assert.Single(entries, e => e.File == FixtureCatalog.Hevc10.FileName);
        Assert.Equal(164, clip.Bytes);
        Assert.Equal("Probe clip: hevc 10-bit", clip.Description);
        Assert.Null(Assert.Single(entries, e => e.File == "old_clip.mp4").Description);
        Assert.Equal("Subtitles, PGS", Assert.Single(entries, e => e.Folder == "downloads" && e.File.EndsWith(".sup", StringComparison.Ordinal)).Description);
    }

    /// <summary>Files still being written are labelled as such rather than as unused.</summary>
    [Fact]
    public void LabelsFilesBeingWritten()
    {
        Write("downloads", "abc.sup.partial", 5);

        Assert.Equal("Being made or downloaded", Assert.Single(FixtureCacheContents.List(_root)).Description);
    }

    /// <summary>Deleting a listed file takes its hash file too; a name the cache doesn't list, such as one outside it, is refused.</summary>
    [Fact]
    public void DeletesOnlyListedFiles()
    {
        Write("sha256_abc", FixtureCatalog.Hevc10.FileName, 100);
        Write("sha256_abc", FixtureCatalog.Hevc10.FileName + ".sha256", 64);
        Write("sha256_abc", "other.mp4", 10);
        var parent = Path.GetDirectoryName(_root);
        Assert.NotNull(parent);
        var outside = Path.Combine(parent, Path.GetFileName(_root) + "-outside.txt");
        File.WriteAllText(outside, "keep");

        Assert.False(FixtureCacheContents.Delete(_root, "..", Path.GetFileName(outside)));
        Assert.False(FixtureCacheContents.Delete(_root, "sha256_abc", FixtureCatalog.Hevc10.FileName + ".sha256"));
        Assert.True(FixtureCacheContents.Delete(_root, "sha256_abc", FixtureCatalog.Hevc10.FileName));

        Assert.Equal(["other.mp4"], FixtureCacheContents.List(_root).Select(e => e.File));
        Assert.True(File.Exists(outside));
        File.Delete(outside);
    }

    /// <summary>Pruning keeps the current build's known clips, samples and known downloads, and deletes the rest.</summary>
    [Fact]
    public void PruneDeletesWhatThisVersionDoesNotUse()
    {
        var current = FixtureBuilder.SanitizeKey("current");
        Write(current, FixtureCatalog.Hevc10.FileName, 1);
        Write(current, FixtureCatalog.Hevc10.FileName + ".sha256", 1);
        Write(current, "old_clip.mp4", 1);
        Write(current, "old_clip.mp4.sha256", 1);
        Write(current, FixtureCatalog.Vp8.FileName + ".partial", 1);
        Write("sha256_older", FixtureCatalog.Hevc10.FileName, 1);
        Write("downloads", SpeedCatalog.ImageSubtitles.Sha256 + ".sup", 1);
        Write("downloads", "0000.sup", 1);

        Assert.Equal(5, FixtureCacheContents.Prune(_root, "current"));

        Assert.Equal(new[] { FixtureCatalog.Hevc10.FileName, SpeedCatalog.ImageSubtitles.Sha256 + ".sup" }.Order(StringComparer.Ordinal), FixtureCacheContents.List(_root).Select(e => e.File).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(_root, current, FixtureCatalog.Hevc10.FileName + ".sha256")));
        Assert.False(Directory.Exists(Path.Combine(_root, "sha256_older")));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Writes a file of the given size into a cache folder.</summary>
    /// <param name="folder">The cache folder.</param>
    /// <param name="name">The file name.</param>
    /// <param name="bytes">Its size.</param>
    private void Write(string folder, string name, int bytes)
    {
        Directory.CreateDirectory(Path.Combine(_root, folder));
        File.WriteAllBytes(Path.Combine(_root, folder, name), new byte[bytes]);
    }
}
