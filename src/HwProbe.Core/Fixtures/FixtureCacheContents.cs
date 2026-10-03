using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Lists the fixture cache and names each file from the catalogs.</summary>
public static class FixtureCacheContents
{
    /// <summary>The extension of the hash file kept beside each clip.</summary>
    private const string HashExtension = ".sha256";

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
        var bySha = Specs().Where(s => s.Spec.Sha256 is not null).GroupBy(s => s.Spec.Sha256!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Description, StringComparer.OrdinalIgnoreCase);
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
                var description = folder.Name == "downloads"
                    ? bySha.GetValueOrDefault(Path.GetFileNameWithoutExtension(file.Name))
                    : byName.GetValueOrDefault(file.Name);
                if (file.Name.Contains(".partial", StringComparison.Ordinal) || file.Name.Contains(".piece.", StringComparison.Ordinal))
                {
                    description = "Being made or downloaded";
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

    /// <summary>Returns what this version caches, by file name.</summary>
    /// <returns>Descriptions by file name.</returns>
    private static Dictionary<string, string> Known() =>
        Specs().GroupBy(s => s.Spec.FileName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Description, StringComparer.Ordinal);

    /// <summary>Returns every clip the probe and performance tests use, with what the page calls it.</summary>
    /// <returns>The clips.</returns>
    private static IEnumerable<(FixtureSpec Spec, string Description)> Specs()
    {
        foreach (var video in SpeedCatalog.Videos.Where(v => v.Fixture is not null))
        {
            yield return (video.Fixture!, video.Name);
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
