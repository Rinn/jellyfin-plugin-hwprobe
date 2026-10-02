using Jellyfin.Plugin.HwProbe.Core.Fixtures;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>The transcodes and decodes a speed run can measure, and their clips.</summary>
/// <remarks>
/// Clips are short and looped with <c>-stream_loop</c>. Transcode sources carry 5.1 AAC, as films usually do, so the
/// audio Jellyfin encodes (and its VBR setting) is part of the cost. Bitrates are jellyfin-web's choices per output
/// height (src/components/qualityOptions.js): the highest is the default, the lowest and highest are compared.
/// </remarks>
public static class SpeedCatalog
{
    private const string Quiet = "-hide_banner -loglevel error -y";

    private const string Audio = "-f lavfi -i sine=frequency=440:sample_rate=48000";

    private const string AudioOut = "-ac 6 -c:a aac -b:a 384k";

    /// <summary>Gets the 1080p H.264 clip with 5.1 audio; veryfast keeps CABAC and B-frames, as real files have.</summary>
    public static FixtureSpec H264At1080 { get; } = new(
        "speed_1080p_h264.mkv",
        "h264",
        8,
        false,
        "libx264",
        $"{Quiet} -f lavfi -i testsrc2=size=1920x1080:rate=24 {Audio} -t 2 -c:v libx264 -preset veryfast -pix_fmt yuv420p {AudioOut}",
        null);

    /// <summary>Gets the 1080i (top field first, 25 frames a second) H.264 clip with 5.1 audio.</summary>
    public static FixtureSpec H264At1080i { get; } = new(
        "speed_1080i_h264.mkv",
        "h264",
        8,
        false,
        "libx264",
        $"{Quiet} -f lavfi -i testsrc2=size=1920x1080:rate=25 {Audio} -t 2 -vf setfield=tff -c:v libx264 -preset veryfast -flags +ildct+ilme -x264-params tff=1 -pix_fmt yuv420p {AudioOut}",
        null)
    {
        Interlaced = true,
    };

    /// <summary>Gets the 2160p HEVC 10-bit HDR10 clip with 5.1 audio.</summary>
    public static FixtureSpec Hdr10At2160 { get; } = new(
        "speed_2160p_hdr10.mkv",
        "hevc",
        10,
        true,
        "libx265",
        $"{Quiet} -f lavfi -i testsrc2=size=3840x2160:rate=24 {Audio} -t 1 -c:v libx265 -preset ultrafast -x265-params log-level=error -pix_fmt yuv420p10le -color_primaries {ColorMetadata.Hdr10.Primaries} -color_trc {ColorMetadata.Hdr10.Transfer} -colorspace {ColorMetadata.Hdr10.Space} {AudioOut}",
        null);

    /// <summary>Gets the 1080p HEVC clip, for decoding.</summary>
    public static FixtureSpec HevcAt1080 { get; } = Decode("speed_1080p_hevc.mkv", "hevc", 8, "libx265", "-c:v libx265 -preset ultrafast -x265-params log-level=error -pix_fmt yuv420p");

    /// <summary>Gets the 1080p HEVC 10-bit clip, for decoding.</summary>
    public static FixtureSpec Hevc10At1080 { get; } = Decode("speed_1080p_hevc10.mkv", "hevc", 10, "libx265", "-c:v libx265 -preset ultrafast -x265-params log-level=error -pix_fmt yuv420p10le");

    /// <summary>Gets the 1080p VP9 clip, for decoding.</summary>
    public static FixtureSpec Vp9At1080 { get; } = Decode("speed_1080p_vp9.webm", "vp9", 8, "libvpx-vp9", "-c:v libvpx-vp9 -deadline realtime -cpu-used 8 -row-mt 1 -b:v 4M -pix_fmt yuv420p");

    /// <summary>Gets the 1080p AV1 clip, for decoding; SVT-AV1 runs its C code, as the matrix clips do.</summary>
    public static FixtureSpec Av1At1080 { get; } = Decode("speed_1080p_av1.mkv", "av1", 8, "libsvtav1", "-c:v libsvtav1 -preset 12 -svtav1-params asm=c -pix_fmt yuv420p");

    /// <summary>Gets an ASS subtitle shown for the whole of any run, made like the matrix one.</summary>
    public static FixtureSpec TextSubtitles { get; } = new(
        "speed_subtitles.ass",
        "ass",
        8,
        false,
        "ass",
        $"{Quiet} -f srt -i \"data:text/plain;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 10:00:00,000\nhwprobe subtitle burn-in\n"))}\" -c:s ass",
        null);

    /// <summary>Gets FFmpeg's PGS sample; ffmpeg has no PGS encoder, so it's downloaded.</summary>
    public static FixtureSpec ImageSubtitles { get; } = new("speed_pgs_sub.sup", "PGSSUB", 8, false, null, string.Empty, null)
    {
        DownloadUrl = new Uri("https://fate-suite.ffmpeg.org/sub/pgs_sub.sup"),
        Sha256 = "ce6d8ed89cf557e34b90d49c5ce955731935c485b93892f69ab276cfe0c36c27",
    };

    /// <summary>Gets every test, in the order the page lists them.</summary>
    public static IReadOnlyList<SpeedTest> All { get; } =
    [
        Transcode("1080p-h264", "1080p H.264 to 720p H.264", H264At1080, "h264"),
        Transcode("1080p-hevc", "1080p H.264 to 720p HEVC", H264At1080, "hevc"),
        Transcode("1080p-av1", "1080p H.264 to 720p AV1", H264At1080, "av1"),
        new("2160p-hdr10", "4K HEVC 10-bit HDR to 1080p H.264, tone-mapped", Hdr10At2160, 24, 3840, 2160)
        {
            OutputCodec = "h264",
            OutputHeight = 1080,
            Bitrate = 8_000_000,
            BitrateRange = (6_000_000, 8_000_000),
            Tonemap = true,
        },
        Transcode("1080i", "1080i H.264 to 720p H.264, deinterlaced", H264At1080i, "h264") with { FrameRate = 25 },
        Transcode("1080p-text-subs", "1080p H.264 to 720p H.264, text subtitles burned in", H264At1080, "h264") with { TextSubtitles = TextSubtitles },
        Transcode("1080p-pgs-subs", "1080p H.264 to 720p H.264, PGS subtitles burned in", H264At1080, "h264") with { ImageSubtitles = ImageSubtitles },
        new("decode-h264", "Decode 1080p H.264", H264At1080, 24, 1920, 1080),
        new("decode-hevc", "Decode 1080p HEVC", HevcAt1080, 24, 1920, 1080),
        new("decode-hevc10", "Decode 1080p HEVC 10-bit", Hevc10At1080, 24, 1920, 1080),
        new("decode-vp9", "Decode 1080p VP9", Vp9At1080, 24, 1920, 1080),
        new("decode-av1", "Decode 1080p AV1", Av1At1080, 24, 1920, 1080),
        new("decode-2160p-hevc10", "Decode 4K HEVC 10-bit", Hdr10At2160, 24, 3840, 2160),
    ];

    /// <summary>Gets the tests run when none are chosen.</summary>
    public static IReadOnlyList<string> Default { get; } = ["1080p-h264"];

    /// <summary>Returns the test with a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The test, or null for an unknown key.</returns>
    public static SpeedTest? Find(string key) => All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    /// <summary>A 1080p transcode to 720p at jellyfin-web's top 720p bitrate.</summary>
    /// <param name="key">The key.</param>
    /// <param name="label">The label.</param>
    /// <param name="fixture">The source clip.</param>
    /// <param name="output">The output codec.</param>
    /// <returns>The test.</returns>
    private static SpeedTest Transcode(string key, string label, FixtureSpec fixture, string output) =>
        new(key, label, fixture, 24, 1920, 1080) { OutputCodec = output, OutputHeight = 720, Bitrate = 4_000_000, BitrateRange = (1_500_000, 4_000_000) };

    /// <summary>A 2-second 1080p clip without audio, for decode tests.</summary>
    /// <param name="fileName">The cached file name.</param>
    /// <param name="codec">The codec as Jellyfin reports it.</param>
    /// <param name="bitDepth">The bit depth.</param>
    /// <param name="encoder">The software encoder that makes it.</param>
    /// <param name="encode">The encoder arguments.</param>
    /// <returns>The clip.</returns>
    private static FixtureSpec Decode(string fileName, string codec, int bitDepth, string encoder, string encode) =>
        new(fileName, codec, bitDepth, false, encoder, $"{Quiet} -f lavfi -i testsrc2=size=1920x1080:rate=24 -t 2 {encode}", null);
}
