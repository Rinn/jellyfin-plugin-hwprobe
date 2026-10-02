using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Plain descriptions of what a speed test reads and makes, for the page and the CLI.</summary>
public static class SpeedTestText
{
    /// <summary>Returns which group a test is listed under.</summary>
    /// <param name="test">The test.</param>
    /// <returns><c>pattern</c>, <c>sample</c> or <c>file</c>.</returns>
    public static string Group(SpeedTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return test.File is not null ? "file" : test.Credit is not null ? "sample" : "pattern";
    }

    /// <summary>Describes a video.</summary>
    /// <param name="video">The video.</param>
    /// <returns>e.g. <c>1080p H.264, 24 fps, 5.1 AAC</c>.</returns>
    public static string Input(SpeedVideo video) => Input(SpeedCatalog.Test(video, SpeedCatalog.Outputs[0]));

    /// <summary>Describes the video a test reads.</summary>
    /// <param name="test">The test.</param>
    /// <returns>e.g. <c>1080p H.264, 24 fps, 5.1 AAC</c>.</returns>
    public static string Input(SpeedTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        var video = test.File?.Video;
        var codec = video?.Codec ?? test.Fixture?.Codec ?? "unknown";
        var depth = video?.BitDepth ?? test.Fixture?.BitDepth ?? 8;
        var hdr = video?.IsHdr ?? test.Fixture?.IsHdr10 ?? false;
        List<string> parts =
        [
            $"{Resolution(test.Width, test.Height, test.Interlaced)} {CodecName(codec)}{(depth > 8 ? string.Create(CultureInfo.InvariantCulture, $" {depth}-bit") : string.Empty)}{(hdr ? " HDR" : string.Empty)}",
            string.Create(CultureInfo.InvariantCulture, $"{test.FrameRate:0.###} fps"),
        ];
        if (test.SourceAudio is { } audio)
        {
            parts.Add(audio);
        }

        return string.Join(", ", parts);
    }

    /// <summary>Describes what a test makes from it.</summary>
    /// <param name="test">The test.</param>
    /// <returns>e.g. <c>720p H.264 at 4 Mbps, stereo AAC</c>, or that it only decodes.</returns>
    public static string Output(SpeedTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        if (test.DecodeOnly)
        {
            return "Decoded only, not encoded";
        }

        List<string> parts = [string.Create(CultureInfo.InvariantCulture, $"{Resolution(test.OutputHeight * 16 / 9, test.OutputHeight, false)} {CodecName(test.OutputCodec!)} at {test.Bitrate / 1_000_000.0:0.#} Mbps")];
        if (test.SourceAudio is not null)
        {
            parts.Add("stereo AAC");
        }

        if (test.Tonemap)
        {
            parts.Add("tone-mapped to SDR");
        }

        if (test.Interlaced)
        {
            parts.Add("deinterlaced");
        }

        return string.Join(", ", parts);
    }

    /// <summary>Names a resolution the way players do, by width, so a 1920x800 film is 1080p.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="interlaced">Whether it's interlaced.</param>
    /// <returns>e.g. <c>4K</c>, <c>1080i</c>.</returns>
    internal static string Resolution(int width, int height, bool interlaced)
    {
        var lines = width >= 3200 || height >= 1800 ? 2160 : width >= 1700 || height >= 1000 ? 1080 : width >= 1100 || height >= 700 ? 720 : height;
        return lines == 2160 ? "4K" : string.Create(CultureInfo.InvariantCulture, $"{lines}{(interlaced ? "i" : "p")}");
    }

    /// <summary>Names a codec as Jellyfin's settings do.</summary>
    /// <param name="codec">The codec as Jellyfin stores it.</param>
    /// <returns>e.g. <c>H.264</c>.</returns>
    private static string CodecName(string codec) => codec switch
    {
        "h264" => "H.264",
        "hevc" => "HEVC",
        "av1" => "AV1",
        "vp9" => "VP9",
        "vp8" => "VP8",
        "mpeg2video" => "MPEG-2",
        "mpeg4" => "MPEG-4",
        "vc1" => "VC-1",
        _ => codec.ToUpperInvariant(),
    };
}
