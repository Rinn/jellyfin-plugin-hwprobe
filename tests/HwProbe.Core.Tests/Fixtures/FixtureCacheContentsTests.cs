using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>Listing the fixture cache in <see cref="FixtureCacheContents"/>.</summary>
[Trait("Category", "Unit")]
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

        var entries = FixtureCacheContents.List(_root);

        Assert.Equal(3, entries.Count);
        var clip = Assert.Single(entries, e => e.File == FixtureCatalog.Hevc10.FileName);
        Assert.Equal(164, clip.Bytes);
        Assert.Equal("Probe clip: hevc 10-bit", clip.Description);
        Assert.Null(Assert.Single(entries, e => e.File == "old_clip.mp4").Description);
        Assert.Equal("Subtitles, PGS", Assert.Single(entries, e => e.Folder == "downloads").Description);
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
