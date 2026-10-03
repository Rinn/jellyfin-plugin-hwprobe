using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Keeps each measurement under a hash of everything that went into it, so a later run with the same settings can reuse it.</summary>
/// <param name="directory">Where the measurements are kept.</param>
public sealed class SpeedResultCache(string directory)
{
    /// <summary>Gets where the measurements are kept.</summary>
    public string Directory { get; } = directory;

    /// <summary>Returns the key of one measurement.</summary>
    /// <param name="ffmpegPath">The ffmpeg binary.</param>
    /// <param name="ffmpegVersion">Its version line.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device.</param>
    /// <param name="test">The test.</param>
    /// <param name="cell">The generated cell, with the clip paths and settings.</param>
    /// <param name="speed">The run's method and repeats; its settings count only through the cell, and the time limit not at all, since a measurement it cut short isn't saved.</param>
    /// <returns>A file-name-safe hash.</returns>
    public static string Key(string ffmpegPath, string ffmpegVersion, HwType type, string device, SpeedTest test, ProbeCell cell, SpeedOptions speed)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(speed);

        // One component per line, as Fingerprint does.
        var text = new StringBuilder();
        void Append(string label, string? value) => text.Append(label).Append('=').Append(value ?? "-").Append('\n');
        Append("hwprobe.version", CapabilityReport.CurrentHwProbeVersion);
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

        Append("cell", JsonSerializer.Serialize(Relevant(cell, type), SpeedJsonContext.Default.ProbeCell));
        Append("method", speed.Method.ToString());
        Append("repeats", speed.Repeats.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>Returns the cell with the settings this test can't be affected by put back to their defaults, so changing them doesn't miss the cache.</summary>
    /// <param name="cell">The generated cell.</param>
    /// <param name="type">The backend.</param>
    /// <returns>The cell to key on.</returns>
    /// <remarks>Where each setting takes effect in EncodingHelper (v12.1): CRF in <c>GetVideoQualityParam</c> for libx264 and libx265 only; the enhanced NVDEC and native decoder switches in the cuvid and QSV decoder paths; tone mapping only for HDR sources; downmix only when audio is transcoded.</remarks>
    public static ProbeCell Relevant(ProbeCell cell, HwType type)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var defaults = new ProbeCell(cell.InputCodec, cell.BitDepth, cell.OutputCodec, cell.HardwareDecode, cell.HardwareEncode);
        var encodes = cell.VideoBitrate is not null;
        var software = type == HwType.none || !cell.HardwareEncode;
        var tonemaps = cell.Tonemap || cell.VppTonemap || cell.VideoToolboxTonemap == true;
        var audio = cell.Audio && !cell.AudioCopy;
        return cell with
        {
            H264Crf = encodes && software && cell.OutputCodec == "h264" ? cell.H264Crf : defaults.H264Crf,
            H265Crf = encodes && software && cell.OutputCodec == "hevc" ? cell.H265Crf : defaults.H265Crf,
            EncoderPreset = encodes ? cell.EncoderPreset : null,
            EnhancedNvdec = type == HwType.nvenc ? cell.EnhancedNvdec : defaults.EnhancedNvdec,
            PreferNativeDecoder = type == HwType.qsv ? cell.PreferNativeDecoder : defaults.PreferNativeDecoder,
            Bwdif = cell.Interlaced && cell.Bwdif,
            DoubleRate = cell.Interlaced && cell.DoubleRate,
            TonemapAlgorithm = tonemaps ? cell.TonemapAlgorithm : null,
            TonemapMode = tonemaps ? cell.TonemapMode : null,
            TonemapRange = tonemaps ? cell.TonemapRange : null,
            TonemapDesat = tonemaps ? cell.TonemapDesat : null,
            TonemapPeak = tonemaps ? cell.TonemapPeak : null,
            TonemapParam = tonemaps ? cell.TonemapParam : null,
            AudioVbr = audio && cell.AudioVbr,
            DownmixAlgorithm = audio ? cell.DownmixAlgorithm : null,
            DownmixBoost = audio ? cell.DownmixBoost : null,
        };
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

    /// <summary>Deletes measurements another version or ffmpeg made, which no key can match any more.</summary>
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
                if (entry is null || entry.HwProbeVersion != CapabilityReport.CurrentHwProbeVersion || entry.FfmpegVersion != ffmpegVersion)
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
