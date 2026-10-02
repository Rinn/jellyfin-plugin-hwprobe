using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>The tests a real file offers: the usual client targets, decoding, and burning in each subtitle track.</summary>
public static class SpeedFileTests
{
    /// <summary>Returns a file's tests.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The tests, keyed <c>file-…</c>.</returns>
    public static IReadOnlyList<SpeedTest> For(SpeedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var video = file.Video;
        var hdr = video.IsHdr ? ", tone-mapped" : string.Empty;
        SpeedTest Transcode(string key, string label, string codec, int height, int bitrate, (int, int) range) =>
            new(key, label, null, video.FrameRate, video.Width, video.Height)
            {
                File = file,
                OutputCodec = codec,
                OutputHeight = height,
                Bitrate = bitrate,
                BitrateRange = range,
                Tonemap = video.IsHdr,
            };

        List<SpeedTest> tests =
        [
            Transcode("file-720p-h264", $"{file.Name} to 720p H.264{hdr}", "h264", 720, 4_000_000, (1_500_000, 4_000_000)),
        ];
        if (video.Height >= 1080)
        {
            tests.Add(Transcode("file-1080p-h264", $"{file.Name} to 1080p H.264{hdr}", "h264", 1080, 8_000_000, (6_000_000, 8_000_000)));
        }

        tests.Add(Transcode("file-720p-hevc", $"{file.Name} to 720p HEVC{hdr}", "hevc", 720, 4_000_000, (1_500_000, 4_000_000)));
        tests.Add(Transcode("file-720p-av1", $"{file.Name} to 720p AV1{hdr}", "av1", 720, 4_000_000, (1_500_000, 4_000_000)));
        tests.Add(new("file-decode", $"Decode {file.Name}", null, video.FrameRate, video.Width, video.Height) { File = file });
        foreach (var subtitle in file.Subtitles)
        {
            var name = string.IsNullOrEmpty(subtitle.Title) ? subtitle.Codec : $"{subtitle.Title} ({subtitle.Codec})";
            tests.Add(Transcode(string.Create(CultureInfo.InvariantCulture, $"file-subs-{subtitle.Index}"), $"{file.Name} to 720p H.264, {name} subtitles burned in", "h264", 720, 4_000_000, (1_500_000, 4_000_000)) with { FileSubtitle = subtitle });
        }

        return tests;
    }
}
