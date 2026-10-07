using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>The transcodes and decodes a speed run can measure, and their clips, from <see cref="Catalog.Default"/>.</summary>
public static class SpeedCatalog
{
    /// <summary>The codec image outputs encode to, as MediaEncoder's image extraction asks EncodingHelper for it (v12.2).</summary>
    public const string ImageCodec = "mjpeg";

    /// <summary>Gets every video, in the order the page lists them; the library video is added from a chosen file.</summary>
    public static IReadOnlyList<SpeedVideo> Videos { get; } = [.. Catalog.Default.Videos.Select(Video)];

    /// <summary>Gets every audio input, in the order the page lists them.</summary>
    public static IReadOnlyList<SpeedAudio> Audios { get; } = [.. Catalog.Default.Audios.Select(Audio)];

    /// <summary>Gets every output, in the order the page lists them: each codec at each quality, then decoding alone, the image outputs, and the audio outputs.</summary>
    public static IReadOnlyList<SpeedOutput> Outputs { get; } =
    [
        .. Catalog.Default.Codecs.SelectMany(c => Catalog.Default.Qualities.Select(q => new SpeedOutput(Catalog.OutputKey(c, q), c.Name + ", " + q.Name, c.Key, q.Bitrate))),
        new SpeedOutput(Catalog.Default.Decode.Key, Catalog.Default.Decode.Label, null, 0),
        .. Catalog.Default.Images.Select(i => new SpeedOutput(i.Key, i.Label, ImageCodec, 0) { Kind = SpeedOutputKind.Images }),
        .. Catalog.Default.AudioOutputs.Select(o => new SpeedOutput(o.Key, o.Label, o.Codec, 0) { Kind = o.Codec is null ? SpeedOutputKind.AudioDecode : SpeedOutputKind.Audio, Channels = o.Channels }),
    ];

    /// <summary>Gets the videos chosen when none are asked for.</summary>
    public static IReadOnlyList<string> DefaultVideos => Catalog.Default.DefaultVideos;

    /// <summary>Gets the outputs chosen when none are asked for.</summary>
    public static IReadOnlyList<string> DefaultOutputs => Catalog.Default.DefaultOutputs;

    /// <summary>Gets the 5.1 AAC track the test videos copy, made before them.</summary>
    public static FixtureSpec TestAudio { get; } = Fixture(Catalog.Default.TestAudio);

    /// <summary>Gets the text (ASS) subtitle the burn-in variation draws.</summary>
    public static FixtureSpec TextSubtitles { get; } = Fixture(Catalog.Default.Subtitles.Text);

    /// <summary>Gets the image (PGS) subtitle the burn-in variation draws.</summary>
    public static FixtureSpec ImageSubtitles { get; } = Fixture(Catalog.Default.Subtitles.Image);

    /// <summary>Gets the key of the library video.</summary>
    public static string LibraryKey => "library";

    /// <summary>Describes a library file as a video.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The video.</returns>
    public static SpeedVideo LibraryVideo(SpeedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var audio = file.Audio is not { } track ? null : $"{SpeedTestText.Channels(track.Channels)} {track.Codec.ToUpperInvariant()}";
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
            Kind = output.Kind,
            Bitrate = output.Bitrate,
            Tonemap = hdr && output.Codec is not null,
        };
    }

    /// <summary>Pairs an audio input with an audio output.</summary>
    /// <param name="audio">The audio input.</param>
    /// <param name="output">The audio output.</param>
    /// <returns>The test, keyed <c>audio|output</c>, at a nominal one frame a second, so its fps is the seconds of audio done each second.</returns>
    public static SpeedTest Test(SpeedAudio audio, SpeedOutput output)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(output);
        return new SpeedTest(audio.Key + "|" + output.Key, audio.Name + " \u2192 " + output.Label, audio.Fixture, 1, 0, 0)
        {
            Name = audio.Name,
            OutputLabel = output.Label,
            OutputCodec = output.Codec,
            Kind = output.Kind,
            OutputChannels = output.Channels,
            AudioInput = audio,
        };
    }

    /// <summary>Returns the test for a <c>video|output</c> or <c>audio|output</c> key among the catalog's inputs.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The test, or null for an unknown key or a video paired with an audio output, or the reverse.</returns>
    public static SpeedTest? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var parts = key.Split('|');
        return parts.Length != 2 || FindOutput(parts[1]) is not { } output ? null
            : output.Audio ? (FindAudio(parts[0]) is { } audio ? Test(audio, output) : null)
            : FindVideo(parts[0]) is { } video ? Test(video, output) : null;
    }

    /// <summary>Returns a catalog video.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The video, or null.</returns>
    public static SpeedVideo? FindVideo(string key) => Videos.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.Ordinal));

    /// <summary>Returns a catalog audio input.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The audio input, or null.</returns>
    public static SpeedAudio? FindAudio(string key) => Audios.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.Ordinal));

    /// <summary>Returns an output.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The output, or null.</returns>
    public static SpeedOutput? FindOutput(string key) => Outputs.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));

    /// <summary>Returns a catalog clip as a fixture.</summary>
    /// <param name="clip">The clip.</param>
    /// <returns>The fixture.</returns>
    private static FixtureSpec Fixture(CatalogClip clip) =>
        new(clip.File, clip.Codec, clip.BitDepth, clip.Hdr10, clip.Encoder, Catalog.Default.Expand(clip.Arguments), null)
        {
            Interlaced = clip.Interlaced,
            DownloadUrl = clip.Download is null ? null : new Uri(clip.Download),
            Sha256 = clip.Sha256,
            Piece = clip.Piece is not { } piece ? null : new FixturePiece(new Uri(piece.Url), piece.HeaderLength, piece.Start, piece.Length, piece.Sha256),
            GenerateTimeout = clip.GenerateMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
            KeepAcrossBuilds = clip.KeepAcrossBuilds,
            PixelFormat = clip.PixelFormat,
            Profile = clip.Profile,
            AudioCodec = clip.AudioCodec,
            AudioChannels = clip.AudioChannels,
            Seconds = clip.Seconds,
        };

    /// <summary>Returns a catalog audio input.</summary>
    /// <param name="audio">The audio input.</param>
    /// <returns>The audio input, with its clip.</returns>
    private static SpeedAudio Audio(CatalogAudio audio)
    {
        var fixture = Fixture(audio.Clip) with { AudioCodec = audio.Codec, AudioChannels = audio.Channels };
        return new SpeedAudio(audio.Key, audio.Name, fixture, audio.Codec, audio.Channels, audio.SampleRate) { Description = audio.Description, Legacy = audio.Legacy };
    }

    /// <summary>Returns a catalog video.</summary>
    /// <param name="video">The video.</param>
    /// <returns>The video, with its clip and, for a sample, its download size and credit.</returns>
    private static SpeedVideo Video(CatalogVideo video)
    {
        var fixture = Fixture(video.Clip);
        var length = video.Clip.Seconds is { } seconds ? string.Create(CultureInfo.InvariantCulture, $"{seconds:0.#} s") : null;
        var size = fixture.Piece?.Size ?? video.Clip.Size;
        var origin = size is { } bytes ? string.Create(CultureInfo.InvariantCulture, $"{length}, {Math.Round(bytes / 1_000_000.0):0} MB") : $"Generated, {length}";
        return new SpeedVideo(video.Key, video.Name, fixture, video.FrameRate, video.Width, video.Height)
        {
            Audio = video.Audio,
            Legacy = video.Legacy,
            Origin = origin,
            Description = video.Description,
            Credit = video.Sample?.Credit,
            CreditHolder = video.Sample?.Holder,
            HolderUrl = video.Sample?.HolderUrl is { } site ? new Uri(site) : null,
            Title = video.Sample?.Title,
            LicenseUrl = video.Sample is null ? null : new Uri(video.Sample.License),
            LicenseName = video.Sample?.LicenseName,
            SourceUrl = video.Sample is null ? null : new Uri(video.Sample.Source),
            SourceName = video.Sample?.SourceName,
            ArticleUrl = video.Sample?.Article is { } article ? new Uri(article) : null,
        };
    }
}
