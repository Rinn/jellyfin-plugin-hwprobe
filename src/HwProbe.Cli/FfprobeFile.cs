using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Describes a video file for a speed run with the ffprobe beside ffmpeg.</summary>
internal static class FfprobeFile
{
    /// <summary>Reads a file's streams.</summary>
    /// <param name="ffmpegPath">The ffmpeg in use; ffprobe is expected beside it.</param>
    /// <param name="path">The video file.</param>
    /// <param name="cancellationToken">Cancels ffprobe.</param>
    /// <returns>The file.</returns>
    /// <exception cref="InvalidOperationException">ffprobe is missing or failed, or the file has no video.</exception>
    public static async Task<SpeedFile> ReadAsync(string ffmpegPath, string path, CancellationToken cancellationToken)
    {
        var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var ffprobe = Path.Join(Path.GetDirectoryName(ffmpegPath), name);
        if (!File.Exists(ffprobe))
        {
            throw new InvalidOperationException($"No {name} beside {ffmpegPath}.");
        }

        var start = new ProcessStartInfo(ffprobe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };

        // As FfmpegRunner does, so its messages and numbers don't follow the host's language.
        start.Environment["LC_ALL"] = "C";
        foreach (var argument in new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", path })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe failed on {path}: {(await stderr).Trim()}");
        }

        return Parse(path, await stdout);
    }

    /// <summary>Parses ffprobe's JSON into the stream details Jellyfin keeps.</summary>
    /// <param name="path">The video's path.</param>
    /// <param name="json">ffprobe's <c>-show_streams -show_format -of json</c> output.</param>
    /// <returns>The file.</returns>
    /// <exception cref="InvalidOperationException">The file has no video stream.</exception>
    internal static SpeedFile Parse(string path, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var streams = root.TryGetProperty("streams", out var list) ? list.EnumerateArray().ToList() : [];
        var video = streams.FirstOrDefault(s => Text(s, "codec_type") == "video" && !IsAttachedPicture(s));
        if (video.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"{path} has no video stream.");
        }

        var audio = streams.FirstOrDefault(s => Text(s, "codec_type") == "audio");
        var pixelFormat = Text(video, "pix_fmt");
        var duration = root.TryGetProperty("format", out var format) && double.TryParse(Text(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
        return new SpeedFile(path, Path.GetFileNameWithoutExtension(path), TimeSpan.FromSeconds(duration), new SpeedFileVideo(
            Number(video, "index"),
            Text(video, "codec_name") ?? "unknown",
            BitDepth(video, pixelFormat),
            Number(video, "width"),
            Number(video, "height"),
            FrameRate(Text(video, "avg_frame_rate") ?? Text(video, "r_frame_rate")))
        {
            Profile = Text(video, "profile"),
            PixelFormat = pixelFormat,
            Interlaced = Text(video, "field_order") is { } order && order != "progressive" && order != "unknown",
            ColorTransfer = Text(video, "color_transfer"),
            ColorPrimaries = Text(video, "color_primaries"),
            ColorSpace = Text(video, "color_space"),

            // ffprobe gives 0:1 when the file doesn't say.
            AspectRatio = Text(video, "display_aspect_ratio") is { } aspect && aspect != "0:1" ? aspect : null,
        })
        {
            Audio = audio.ValueKind == JsonValueKind.Undefined ? null : new SpeedFileAudio(Number(audio, "index"), Text(audio, "codec_name") ?? "aac", Number(audio, "channels")),
            Subtitles = [.. streams.Where(s => Text(s, "codec_type") == "subtitle").Select(Subtitle)],
        };
    }

    /// <summary>Describes a subtitle stream, with Jellyfin's codec names for image subtitles.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The subtitle.</returns>
    private static SpeedFileSubtitle Subtitle(JsonElement stream)
    {
        var codec = Text(stream, "codec_name") ?? "unknown";
        var image = codec is "hdmv_pgs_subtitle" or "dvd_subtitle" or "dvb_subtitle" or "xsub";
        var language = stream.TryGetProperty("tags", out var tags) ? Text(tags, "language") ?? Text(tags, "title") : null;
        var jellyfinCodec = codec switch
        {
            "hdmv_pgs_subtitle" => "PGSSUB",
            "dvd_subtitle" => "DVDSUB",
            "dvb_subtitle" => "DVBSUB",
            _ => codec,
        };
        return new SpeedFileSubtitle(Number(stream, "index"), jellyfinCodec, !image, language);
    }

    /// <summary>Reads the bit depth, from the stream or its pixel format.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="pixelFormat">The pixel format.</param>
    /// <returns>The bit depth.</returns>
    private static int BitDepth(JsonElement stream, string? pixelFormat) =>
        int.TryParse(Text(stream, "bits_per_raw_sample"), CultureInfo.InvariantCulture, out var bits) && bits > 0 ? bits
        : pixelFormat?.Contains("12", StringComparison.Ordinal) == true ? 12
        : pixelFormat?.Contains("10", StringComparison.Ordinal) == true ? 10
        : 8;

    /// <summary>Parses a rational frame rate such as <c>24000/1001</c>.</summary>
    /// <param name="rate">The rate.</param>
    /// <returns>The frame rate, or 24 when unreadable.</returns>
    private static float FrameRate(string? rate)
    {
        var parts = rate?.Split('/') ?? [];
        return parts.Length == 2 && float.TryParse(parts[0], CultureInfo.InvariantCulture, out var n) && float.TryParse(parts[1], CultureInfo.InvariantCulture, out var d) && n > 0 && d > 0 ? n / d : 24;
    }

    /// <summary>Reports whether a video stream is cover art rather than video.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>True for an attached picture.</returns>
    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition) && disposition.TryGetProperty("attached_pic", out var picture) && picture.ValueKind == JsonValueKind.Number && picture.GetInt32() == 1;

    /// <summary>Reads a property as text.</summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The text, or null.</returns>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString() : null;

    /// <summary>Reads a property as a whole number.</summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number, or 0.</returns>
    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
