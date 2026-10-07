using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Lists the fixture cache and names each file from the catalogs.</summary>
public static class FixtureCacheContents
{
    /// <summary>The extension of the hash file kept beside each clip.</summary>
    private const string HashExtension = ".sha256";

    /// <summary>The folder for clips kept across ffmpeg builds.</summary>
    private const string SamplesFolder = "samples";

    /// <summary>The folder for downloads, named by SHA-256.</summary>
    private const string DownloadsFolder = "downloads";

    /// <summary>The description of a file still being written.</summary>
    private const string InProgress = "Being made or downloaded";

    /// <summary>Lists every cached file, its hash file folded in.</summary>
    /// <param name="directory">The fixture cache directory.</param>
    /// <returns>The entries by folder and file name; empty when nothing is cached.</returns>
    public static IReadOnlyList<CacheEntry> List(string? directory)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return [];
        }

        var byName = Known();
        Dictionary<string, string> bySha = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (spec, description) in Specs())
        {
            if (spec.Sha256 is { } sha256)
            {
                bySha.TryAdd(sha256, description);
            }
        }

        List<CacheEntry> entries = [];
        foreach (var folder in new DirectoryInfo(directory).EnumerateDirectories())
        {
            // A purge or a new ffmpeg build can remove a folder while it's listed.
            List<FileInfo> files;
            try
            {
                files = [.. folder.EnumerateFiles()];
            }
            catch (Exception e) when (e is DirectoryNotFoundException or UnauthorizedAccessException)
            {
                continue;
            }

            var hashes = files.Where(f => f.Name.EndsWith(HashExtension, StringComparison.Ordinal)).ToDictionary(f => f.Name[..^HashExtension.Length], f => f.Length, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var isHash = file.Name.EndsWith(HashExtension, StringComparison.Ordinal);
                if (isHash && files.Any(f => f.Name == file.Name[..^HashExtension.Length]))
                {
                    continue;
                }

                // Downloads are named by their SHA-256, so the name says what they are.
                var description = folder.Name == DownloadsFolder
                    ? bySha.GetValueOrDefault(Path.GetFileNameWithoutExtension(file.Name))
                    : byName.GetValueOrDefault(file.Name);
                if (file.Name.Contains(".partial", StringComparison.Ordinal) || file.Name.Contains(".piece.", StringComparison.Ordinal))
                {
                    description = InProgress;
                }
                else if (isHash)
                {
                    description = null;
                }

                entries.Add(new CacheEntry(folder.Name, file.Name, file.Length + hashes.GetValueOrDefault(file.Name), file.LastWriteTimeUtc, description));
            }
        }

        return [.. entries.OrderBy(e => e.Folder, StringComparer.Ordinal).ThenBy(e => e.File, StringComparer.Ordinal)];
    }

    /// <summary>Deletes one listed file and its hash file.</summary>
    /// <param name="directory">The fixture cache directory.</param>
    /// <param name="folder">The entry's folder, as <see cref="List"/> gives it.</param>
    /// <param name="file">The entry's file name, as <see cref="List"/> gives it.</param>
    /// <returns>False when no such entry is listed, so a name can't reach outside the cache.</returns>
    /// <remarks>Call only while nothing else builds clips.</remarks>
    public static bool Delete(string? directory, string folder, string file)
    {
        if (directory is null || !List(directory).Any(e => e.Folder == folder && e.File == file))
        {
            return false;
        }

        var path = Path.Combine(directory, folder, file);
        File.Delete(path);
        File.Delete(path + HashExtension);
        return true;
    }

    /// <summary>Deletes what this version no longer uses: other ffmpeg builds' clips, unknown files and leftovers of interrupted writes.</summary>
    /// <param name="directory">The fixture cache directory.</param>
    /// <param name="cacheKey">The current ffmpeg build's key, as <see cref="FixtureBuilder.BuildAsync"/> takes it.</param>
    /// <returns>How many files were deleted.</returns>
    /// <remarks>Call only while nothing else builds clips; a file being written counts as a leftover.</remarks>
    public static int Prune(string? directory, string cacheKey)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return 0;
        }

        var keep = new HashSet<string>(StringComparer.Ordinal) { SamplesFolder, DownloadsFolder, FixtureBuilder.SanitizeKey(cacheKey) };
        var deleted = 0;
        foreach (var folder in new DirectoryInfo(directory).EnumerateDirectories())
        {
            try
            {
                if (!keep.Contains(folder.Name))
                {
                    deleted += folder.EnumerateFiles("*", SearchOption.AllDirectories).Count();
                    folder.Delete(recursive: true);
                    continue;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in List(directory).Where(e => e.Folder == folder.Name && (e.Description is null || e.Description == InProgress)))
            {
                foreach (var name in new[] { entry.File, entry.File + HashExtension })
                {
                    try
                    {
                        var path = Path.Combine(folder.FullName, name);
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            deleted++;
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        // Left for the next run.
                    }
                }
            }
        }

        return deleted;
    }

    /// <summary>Returns what this version caches, by file name.</summary>
    /// <returns>Descriptions by file name.</returns>
    private static Dictionary<string, string> Known() =>
        Specs().GroupBy(s => s.Spec.FileName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Description, StringComparer.Ordinal);

    /// <summary>Returns every clip the probe and performance tests use, with what the page calls it.</summary>
    /// <returns>The clips.</returns>
    private static IEnumerable<(FixtureSpec Spec, string Description)> Specs()
    {
        foreach (var video in SpeedCatalog.Videos)
        {
            if (video.Fixture is { } fixture)
            {
                yield return (fixture, video.Name);
            }
        }

        yield return (SpeedCatalog.TestAudio, "Test audio, 5.1 AAC");
        yield return (SpeedCatalog.TextSubtitles, "Subtitles, text");
        yield return (SpeedCatalog.ImageSubtitles, "Subtitles, PGS");
        foreach (var spec in FixtureCatalog.All)
        {
            yield return (spec, "Probe clip: " + ProbeName(spec));
        }
    }

    /// <summary>Describes a probe clip from its stream properties.</summary>
    /// <param name="spec">The clip.</param>
    /// <returns>For example <c>hevc 10-bit Rext</c>.</returns>
    private static string ProbeName(FixtureSpec spec) =>
        string.Join(' ', new[] { spec.Codec, spec.Codec is "ass" ? null : spec.BitDepth + "-bit", spec.PixelFormat, spec.Profile, spec.IsHdr10 ? "HDR10" : null, spec.Interlaced ? "interlaced" : null }.Where(p => p is not null));
}
