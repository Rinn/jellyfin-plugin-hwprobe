using Jellyfin.Plugin.HwProbe.Core.Fixtures;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>The transcodes and decodes a speed run can measure, and their clips.</summary>
/// <remarks>
/// Clips are 10 s (5 s at 4K), as long as one copy's run, and looped with <c>-stream_loop</c> for longer runs. Transcode sources carry 5.1 AAC, as films usually do, so the
/// audio Jellyfin encodes (and its VBR setting) is part of the cost. Bitrates are jellyfin-web's choices per output
/// height (src/components/qualityOptions.js): the highest is the default, the lowest and highest are compared.
/// </remarks>
public static class SpeedCatalog
{
    private const string Quiet = "-hide_banner -loglevel error -y";

    private const string Audio = "-f lavfi -i sine=frequency=440:sample_rate=48000";

    private const string AudioOut = "-ac 6 -c:a aac -b:a 384k";

    private const string SampleAudio = "-c:a aac -ac 2 -b:a 192k";

    private static readonly Uri _ccBy3 = new("https://creativecommons.org/licenses/by/3.0/");

    private static readonly Uri _ccBy4 = new("https://creativecommons.org/licenses/by/4.0/");

    /// <summary>Gets the 1080p H.264 clip with 5.1 audio; veryfast keeps CABAC and B-frames, as real files have.</summary>
    public static FixtureSpec H264At1080 { get; } = new(
        "speed_1080p_h264.mkv",
        "h264",
        8,
        false,
        "libx264",
        $"{Quiet} -f lavfi -i testsrc2=size=1920x1080:rate=24 {Audio} -t 10 -c:v libx264 -preset veryfast -pix_fmt yuv420p {AudioOut}",
        null);

    /// <summary>Gets the 1080i (top field first, 25 frames a second) H.264 clip with 5.1 audio.</summary>
    public static FixtureSpec H264At1080i { get; } = new(
        "speed_1080i_h264.mkv",
        "h264",
        8,
        false,
        "libx264",
        $"{Quiet} -f lavfi -i testsrc2=size=1920x1080:rate=25 {Audio} -t 10 -vf setfield=tff -c:v libx264 -preset veryfast -flags +ildct+ilme -x264-params tff=1 -pix_fmt yuv420p {AudioOut}",
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
        $"{Quiet} -f lavfi -i testsrc2=size=3840x2160:rate=24 {Audio} -t 5 -c:v libx265 -preset ultrafast -x265-params log-level=error -pix_fmt yuv420p10le -color_primaries {ColorMetadata.Hdr10.Primaries} -color_trc {ColorMetadata.Hdr10.Transfer} -colorspace {ColorMetadata.Hdr10.Space} {AudioOut}",
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

    /// <summary>Gets a live-action sample: Tears of Steel (6:39 to 7:11), CC BY 3.0, from Wikimedia Commons.</summary>
    public static FixtureSpec LiveAction { get; } = Sample(
        "sample_live_action.mkv",
        new(Commons("transcoded/1/10/Tears_of_Steel_in_4k_-_Official_Blender_Foundation_release.webm/Tears_of_Steel_in_4k_-_Official_Blender_Foundation_release.webm.1080p.vp9.webm"), 530, 131614236, 9768249, "b3156d91f1c27710e6455b3c5d564875c1ea7b960d7d0c082971a7162bcb81f5"));

    /// <summary>Gets a digital-animation sample: Sintel (5:30 to 6:03), CC BY 3.0, from Wikimedia Commons.</summary>
    public static FixtureSpec DigitalAnimation { get; } = Sample(
        "sample_digital_animation.mkv",
        new(Commons("transcoded/f/f1/Sintel_movie_4K.webm/Sintel_movie_4K.webm.1080p.vp9.webm"), 559, 96184958, 10681045, "399ed227a21fd5f9e2431e0eeae6407243fcf545422d18c6ab41dcb667426435"));

    /// <summary>Gets an anime sample: Sol Levante (1:31 to 2:01), CC BY 4.0, from Wikimedia Commons.</summary>
    public static FixtureSpec Anime { get; } = Sample(
        "sample_anime.mkv",
        new(Commons("transcoded/4/44/Sol_Levante.webm/Sol_Levante.webm.1080p.vp9.webm"), 991, 45210548, 13536666, "a97d4180958b16da1dcd69ec429734b779979003aac6655d0b53ae7db0ff3575"));

    /// <summary>Gets a 4K HDR10 anime sample: Sol Levante (1:30 to 2:00) from Wikimedia Commons' 4K HDR10 AV1 copy, CC BY 4.0.</summary>
    /// <remarks>Encoded to 4K HEVC 10-bit HDR10, as 4K HDR files usually are. The 4K encode is slow on a small CPU.</remarks>
    public static FixtureSpec Anime4k { get; } = new(
        "sample_anime_4k_hdr10.mkv",
        "hevc",
        10,
        true,
        "libx265",
        $"{Quiet} -i {{piece}} -t 30 -vf format=yuv420p10le -c:v libx265 -preset veryfast -crf 18 -x265-params log-level=error:hdr10=1:repeat-headers=1 -color_primaries {ColorMetadata.Hdr10.Primaries} -color_trc {ColorMetadata.Hdr10.Transfer} -colorspace {ColorMetadata.Hdr10.Space} {SampleAudio}",
        null)
    {
        Piece = new(Commons("4/44/Sol_Levante.webm"), 1022, 563867512, 108468322, "eb5c77933efab30d9a1e09f5c554a3492a4a94623d32bf77fdcba43d70e7462d"),
        GenerateTimeout = TimeSpan.FromHours(1),
        KeepAcrossBuilds = true,
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
        Transcode("sample-live-action", $"Live action (Tears of Steel, downloads {Megabytes(LiveAction)}) to 720p H.264", LiveAction, "h264") with { Height = 858, Credit = "Tears of Steel, Blender Foundation (mango.blender.org), CC BY 3.0, via Wikimedia Commons", LicenseUrl = _ccBy3 },
        Transcode("sample-digital-animation", $"Digital animation (Sintel, downloads {Megabytes(DigitalAnimation)}) to 720p H.264", DigitalAnimation, "h264") with { Height = 818, Credit = "Sintel, Blender Foundation (durian.blender.org), CC BY 3.0, via Wikimedia Commons", LicenseUrl = _ccBy3 },
        Transcode("sample-anime", $"Anime (Sol Levante, downloads {Megabytes(Anime)}) to 720p H.264", Anime, "h264") with { Credit = "Sol Levante, Netflix and Production I.G, CC BY 4.0, via Wikimedia Commons", LicenseUrl = _ccBy4 },
        new("sample-anime-4k", $"Anime 4K HDR (Sol Levante, downloads {Megabytes(Anime4k)}) to 1080p H.264, tone-mapped", Anime4k, 24, 3840, 2160)
        {
            OutputCodec = "h264",
            OutputHeight = 1080,
            Bitrate = 8_000_000,
            BitrateRange = (6_000_000, 8_000_000),
            Tonemap = true,
            Credit = "Sol Levante, Netflix and Production I.G, CC BY 4.0, via Wikimedia Commons",
            LicenseUrl = _ccBy4,
        },
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

    /// <summary>A 30-second piece of a film on Wikimedia Commons, checked against its pinned hash and encoded the same way for every 1080p sample.</summary>
    /// <param name="fileName">The cached file name.</param>
    /// <param name="piece">The pinned piece of Commons' 1080p VP9 transcode.</param>
    /// <returns>The clip; only fetched when a test that uses it is chosen.</returns>
    private static FixtureSpec Sample(string fileName, FixturePiece piece) => new(
        fileName,
        "h264",
        8,
        false,
        "libx264",
        $"{Quiet} -i {{piece}} -t 30 -vf fps=24,scale=1920:-2 -c:v libx264 -preset veryfast -crf 18 -maxrate 10M -bufsize 20M -pix_fmt yuv420p {SampleAudio}",
        null)
    {
        Piece = piece,
        KeepAcrossBuilds = true,
    };

    /// <summary>Returns a file's URL on Wikimedia Commons.</summary>
    /// <param name="path">The path under upload.wikimedia.org/wikipedia/commons/.</param>
    /// <returns>The URL.</returns>
    private static Uri Commons(string path) => new("https://upload.wikimedia.org/wikipedia/commons/" + path);

    /// <summary>Formats a sample's download size for its label.</summary>
    /// <param name="spec">The sample.</param>
    /// <returns>e.g. <c>about 11 MB</c>.</returns>
    private static string Megabytes(FixtureSpec spec) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"about {Math.Round(spec.Piece!.Size / 1_000_000.0):0} MB");

    /// <summary>A 10-second 1080p clip without audio, for decode tests.</summary>
    /// <param name="fileName">The cached file name.</param>
    /// <param name="codec">The codec as Jellyfin reports it.</param>
    /// <param name="bitDepth">The bit depth.</param>
    /// <param name="encoder">The software encoder that makes it.</param>
    /// <param name="encode">The encoder arguments.</param>
    /// <returns>The clip.</returns>
    private static FixtureSpec Decode(string fileName, string codec, int bitDepth, string encoder, string encode) =>
        new(fileName, codec, bitDepth, false, encoder, $"{Quiet} -f lavfi -i testsrc2=size=1920x1080:rate=24 -t 10 {encode}", null);
}
