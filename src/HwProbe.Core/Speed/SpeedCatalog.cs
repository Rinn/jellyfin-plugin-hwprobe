using System.Globalization;
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

    private const string Surround = "5.1 AAC";

    private const string Stereo = "stereo AAC";

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

    /// <summary>Gets every video, in the order the page lists them; the library video is added from a chosen file.</summary>
    public static IReadOnlyList<SpeedVideo> Videos { get; } =
    [
        Pattern("pattern", "Test video", H264At1080, Surround),
        Pattern("pattern-hevc", "Test video, HEVC", HevcAt1080, null),
        Pattern("pattern-hevc10", "Test video, HEVC 10-bit", Hevc10At1080, null),
        Pattern("pattern-vp9", "Test video, VP9", Vp9At1080, null),
        Pattern("pattern-av1", "Test video, AV1", Av1At1080, null),
        Pattern("pattern-1080i", "Test video, interlaced", H264At1080i, Surround) with { FrameRate = 25 },
        Pattern("pattern-4k-hdr", "Test video, 4K HDR", Hdr10At2160, Surround) with { Width = 3840, Height = 2160 },
        Sample("live-action", "Live action", LiveAction, 858, "Tears of Steel, Blender Foundation (mango.blender.org), CC BY 3.0, via Wikimedia Commons", _ccBy3, "Tears_of_Steel_in_4k_-_Official_Blender_Foundation_release.webm"),
        Sample("digital-animation", "3D animation", DigitalAnimation, 818, "Sintel, Blender Foundation (durian.blender.org), CC BY 3.0, via Wikimedia Commons", _ccBy3, "Sintel_movie_4K.webm"),
        Sample("anime", "Animation", Anime, 1080, "Sol Levante, Netflix and Production I.G, CC BY 4.0, via Wikimedia Commons", _ccBy4, "Sol_Levante.webm"),
        Sample("anime-4k", "Animation 4K HDR", Anime4k, 2160, "Sol Levante, Netflix and Production I.G, CC BY 4.0, via Wikimedia Commons", _ccBy4, "Sol_Levante.webm") with { Width = 3840 },
    ];

    /// <summary>Gets every output, in the order the page lists them.</summary>
    /// <remarks>Bitrates are jellyfin-web's top choices for the height (src/components/qualityOptions.js); the range is its lowest and highest.</remarks>
    public static IReadOnlyList<SpeedOutput> Outputs { get; } =
    [
        new("720p-h264", "720p H.264, 4 Mbps", "h264", 720, 4_000_000, (1_500_000, 4_000_000)) { Detail = Encoded("720p", "H.264") },
        new("720p-hevc", "720p HEVC, 4 Mbps", "hevc", 720, 4_000_000, (1_500_000, 4_000_000)) { Detail = Encoded("720p", "HEVC") },
        new("720p-av1", "720p AV1, 4 Mbps", "av1", 720, 4_000_000, (1_500_000, 4_000_000)) { Detail = Encoded("720p", "AV1") },
        new("1080p-h264", "1080p H.264, 8 Mbps", "h264", 1080, 8_000_000, (6_000_000, 8_000_000)) { Detail = Encoded("1080p", "H.264") },
        new("720p-h264-text", "720p H.264, 4 Mbps, text subtitles", "h264", 720, 4_000_000, (1_500_000, 4_000_000)) { Subtitles = "text", Detail = Encoded("720p", "H.264") + ", ASS subtitles burned into the picture" },
        new("720p-h264-pgs", "720p H.264, 4 Mbps, PGS subtitles", "h264", 720, 4_000_000, (1_500_000, 4_000_000)) { Subtitles = "image", Detail = Encoded("720p", "H.264") + ", PGS subtitles burned into the picture" },
        new("decode", "Decode only", null, 0, 0, (0, 0)) { Detail = "Video decoded, nothing encoded" },
    ];

    /// <summary>Gets the videos chosen when none are asked for.</summary>
    public static IReadOnlyList<string> DefaultVideos { get; } = ["pattern"];

    /// <summary>Gets the outputs chosen when none are asked for.</summary>
    public static IReadOnlyList<string> DefaultOutputs { get; } = ["720p-h264"];

    /// <summary>Gets the key of the library video.</summary>
    public static string LibraryKey => "library";

    /// <summary>Describes a library file as a video.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The video.</returns>
    public static SpeedVideo LibraryVideo(SpeedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var audio = file.Audio is not { } track ? null : string.Create(CultureInfo.InvariantCulture, $"{track.Channels switch { 1 => "mono", 2 => "stereo", 6 => "5.1", 8 => "7.1", var n => n + " channel" }} {track.Codec.ToUpperInvariant()}");
        return new SpeedVideo(LibraryKey, file.Name, null, file.Video.FrameRate, file.Video.Width, file.Video.Height) { File = file, Audio = audio, Origin = "Library" };
    }

    /// <summary>Pairs a video with an output.</summary>
    /// <param name="video">The video.</param>
    /// <param name="output">The output.</param>
    /// <returns>The test, keyed <c>video|output</c>.</returns>
    public static SpeedTest Test(SpeedVideo video, SpeedOutput output)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(output);
        var hdr = video.File?.Video.IsHdr ?? video.Fixture?.IsHdr10 ?? false;
        return new SpeedTest(video.Key + "|" + output.Key, video.Name + " \u2192 " + output.Label, video.Fixture, video.FrameRate, video.Width, video.Height)
        {
            File = video.File,
            Name = video.Name,
            OutputLabel = output.Label,
            SourceAudio = video.Audio,
            Credit = video.Credit,
            LicenseUrl = video.LicenseUrl,
            OutputCodec = output.Codec,
            OutputHeight = output.Height,
            Bitrate = output.Bitrate,
            BitrateRange = output.BitrateRange,
            Tonemap = hdr && output.Codec is not null,
            TextSubtitles = output.Subtitles == "text" ? TextSubtitles : null,
            ImageSubtitles = output.Subtitles == "image" ? ImageSubtitles : null,
        };
    }

    /// <summary>Returns the test for a <c>video|output</c> key among the catalog's videos.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The test, or null for an unknown key.</returns>
    public static SpeedTest? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var parts = key.Split('|');
        return parts.Length == 2 && FindVideo(parts[0]) is { } video && FindOutput(parts[1]) is { } output ? Test(video, output) : null;
    }

    /// <summary>Returns a catalog video.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The video, or null.</returns>
    public static SpeedVideo? FindVideo(string key) => Videos.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.Ordinal));

    /// <summary>Returns an output.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The output, or null.</returns>
    public static SpeedOutput? FindOutput(string key) => Outputs.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));

    /// <summary>Describes an encoded output.</summary>
    /// <param name="resolution">e.g. <c>720p</c>.</param>
    /// <param name="codec">e.g. <c>H.264</c>.</param>
    /// <returns>The detail; HDR input is also tone-mapped to SDR.</returns>
    private static string Encoded(string resolution, string codec) => $"Video encoded to {resolution} {codec}, audio to stereo AAC; HDR is tone-mapped";

    /// <summary>A test video made on the server.</summary>
    /// <param name="key">The key.</param>
    /// <param name="name">The name.</param>
    /// <param name="fixture">The clip.</param>
    /// <param name="audio">Its audio, or null.</param>
    /// <returns>The video.</returns>
    private static SpeedVideo Pattern(string key, string name, FixtureSpec fixture, string? audio) =>
        new(key, name, fixture, 24, 1920, 1080) { Audio = audio, Origin = "Generated" };

    /// <summary>A downloaded sample.</summary>
    /// <param name="key">The key.</param>
    /// <param name="name">The name.</param>
    /// <param name="fixture">The clip.</param>
    /// <param name="height">Its height after encoding (films are wider than 16:9).</param>
    /// <param name="credit">The credit its licence requires.</param>
    /// <param name="license">Its licence.</param>
    /// <param name="commonsFile">Its file name on Wikimedia Commons, whose page has the credit and licence.</param>
    /// <returns>The video.</returns>
    private static SpeedVideo Sample(string key, string name, FixtureSpec fixture, int height, string credit, Uri license, string commonsFile) =>
        new(key, name, fixture, 24, 1920, height) { Audio = Stereo, Origin = Megabytes(fixture) + " download", Credit = credit, LicenseUrl = license, Title = credit[..credit.IndexOf(',', StringComparison.Ordinal)], SourceUrl = new Uri("https://commons.wikimedia.org/wiki/File:" + commonsFile) };

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
    /// <returns>e.g. <c>11 MB</c>.</returns>
    private static string Megabytes(FixtureSpec spec) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(spec.Piece!.Size / 1_000_000.0):0} MB");

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
