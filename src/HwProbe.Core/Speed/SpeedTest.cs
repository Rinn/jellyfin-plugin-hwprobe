using Jellyfin.Plugin.HwProbe.Core.Fixtures;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>One transcode or decode the speed run can measure.</summary>
/// <param name="Key">Stable key, e.g. <c>1080p-h264</c>.</param>
/// <param name="Label">What the page shows, e.g. <c>1080p H.264 to 720p H.264</c>.</param>
/// <param name="Fixture">The generated source clip, or null for a test on a real file.</param>
/// <param name="FrameRate">The source frame rate, which real time is measured against.</param>
/// <param name="Width">The source width.</param>
/// <param name="Height">The source height.</param>
public sealed record SpeedTest(string Key, string Label, FixtureSpec? Fixture, float FrameRate, int Width, int Height)
{
    /// <summary>Gets the output codec, or null for a decode-only test.</summary>
    public string? OutputCodec { get; init; }

    /// <summary>Gets the output height the client asks for; the width follows at 16:9.</summary>
    public int OutputHeight { get; init; }

    /// <summary>Gets the video bitrate the client asks for, in bits per second.</summary>
    public int Bitrate { get; init; }

    /// <summary>Gets the lowest and highest bitrates jellyfin-web offers for <see cref="OutputHeight"/>.</summary>
    public (int Low, int High) BitrateRange { get; init; }

    /// <summary>Gets a value indicating whether the source is HDR10 and tone-mapped.</summary>
    public bool Tonemap { get; init; }

    /// <summary>Gets a text subtitle clip to burn in, or null.</summary>
    public FixtureSpec? TextSubtitles { get; init; }

    /// <summary>Gets an image subtitle clip to burn in, or null.</summary>
    public FixtureSpec? ImageSubtitles { get; init; }

    /// <summary>Gets the credit a sample's licence requires, or null.</summary>
    public string? Credit { get; init; }

    /// <summary>Gets the real file the test runs on, or null for a generated clip.</summary>
    public SpeedFile? File { get; init; }

    /// <summary>Gets the file's subtitle stream to burn in, or null.</summary>
    public SpeedFileSubtitle? FileSubtitle { get; init; }

    /// <summary>Gets a value indicating whether the source is interlaced.</summary>
    public bool Interlaced => Fixture?.Interlaced ?? File?.Video.Interlaced ?? false;

    /// <summary>Gets where in the source to start: a tenth of the way into a real file, past most intros.</summary>
    public TimeSpan StartAt => File is null ? TimeSpan.Zero : File.Duration / 10;

    /// <summary>Gets a value indicating whether the test only decodes.</summary>
    public bool DecodeOnly => OutputCodec is null;
}
