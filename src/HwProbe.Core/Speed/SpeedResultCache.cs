using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Keeps each measurement under a hash of everything that went into it, so a later run with the same settings can reuse it.</summary>
/// <param name="directory">Where the measurements are kept.</param>
public sealed class SpeedResultCache(string directory)
{
    /// <summary>The way measurements are taken; bump it when SpeedMeter or the command wrapper changes what a measurement reports, so earlier ones aren't reused. Other plugin changes don't matter: the command is keyed.</summary>
    public const int MeasurementVersion = 2;

    /// <summary>Gets where the measurements are kept.</summary>
    public string Directory { get; } = directory;

    /// <summary>Returns the key of one measurement.</summary>
    /// <param name="ffmpegPath">The ffmpeg binary.</param>
    /// <param name="ffmpegVersion">Its version line.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device.</param>
    /// <param name="test">The test, for a library file's size and time.</param>
    /// <param name="command">The generated command line and environment; every setting that reaches ffmpeg is in it.</param>
    /// <param name="speed">The run's method and repeats; the time limit isn't keyed, since a measurement it cut short isn't saved.</param>
    /// <returns>A file-name-safe hash.</returns>
    public static string Key(string ffmpegPath, string ffmpegVersion, HwType type, string device, SpeedTest test, string command, SpeedOptions speed)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(speed);

        // One component per line, as Fingerprint does.
        var text = new StringBuilder();
        void Append(string label, string? value) => text.Append(label).Append('=').Append(value ?? "-").Append('\n');
        Append("measurement", MeasurementVersion.ToString(CultureInfo.InvariantCulture));
        Append("ffmpeg.path", ffmpegPath);
        Append("ffmpeg.version", ffmpegVersion);
        Append("backend", type.ToString());
        Append("device", device);
        Append("test", test.Key);

        // A library file can be replaced under the same path.
        if (test.File is { } file && File.Exists(file.Path))
        {
            var info = new FileInfo(file.Path);
            Append("file", string.Create(CultureInfo.InvariantCulture, $"{file.Path}|{info.Length}|{info.LastWriteTimeUtc:O}"));
        }

        Append("command", command);
        Append("method", speed.Method.ToString());
        Append("repeats", speed.Repeats.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>Returns a saved measurement, or null when there's none or it can't be read.</summary>
    /// <param name="key">The key from <see cref="Key"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry, or null.</returns>
    public async Task<SpeedCacheEntry?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, cancellationToken), SpeedJsonContext.Default.SpeedCacheEntry) : null;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Saves a measurement, atomically.</summary>
    /// <param name="key">The key from <see cref="Key"/>.</param>
    /// <param name="entry">The measurement.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public async Task SaveAsync(string key, SpeedCacheEntry entry, CancellationToken cancellationToken)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(key);
        var temp = path + ".partial";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(entry, SpeedJsonContext.Default.SpeedCacheEntry), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Deletes measurements another way of measuring or another ffmpeg made, which no key can match any more.</summary>
    /// <param name="ffmpegVersion">The current ffmpeg version line.</param>
    /// <returns>How many were deleted.</returns>
    public int Prune(string ffmpegVersion)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory))
        {
            try
            {
                var entry = file.EndsWith(".json", StringComparison.Ordinal) ? JsonSerializer.Deserialize(File.ReadAllText(file), SpeedJsonContext.Default.SpeedCacheEntry) : null;
                if (entry is null || entry.Method != MeasurementVersion || entry.FfmpegVersion != ffmpegVersion)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception e) when (e is JsonException)
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left for the next run.
            }
        }

        return deleted;
    }

    /// <summary>Returns a measurement's file.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The path.</returns>
    private string PathFor(string key) => Path.Combine(Directory, key + ".json");
}
