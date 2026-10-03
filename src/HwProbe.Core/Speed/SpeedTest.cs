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

    /// <summary>Gets the video bitrate the client asks for, in bits per second.</summary>
    public int Bitrate { get; init; }

    /// <summary>Gets a value indicating whether the source is HDR10 and tone-mapped.</summary>
    public bool Tonemap { get; init; }

    /// <summary>Gets a short name for the test's source, e.g. <c>Test pattern</c> or <c>Live action: Tears of Steel</c>; the key when unset.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the output's label, e.g. <c>720p H.264, 4 Mbps</c>.</summary>
    public string? OutputLabel { get; init; }

    /// <summary>Gets the source's audio as the page describes it, e.g. <c>5.1 AAC</c>, or null for none.</summary>
    public string? SourceAudio { get; init; }

    /// <summary>Gets the credit a sample's licence requires, or null.</summary>
    public string? Credit { get; init; }

    /// <summary>Gets the sample's licence, or null.</summary>
    public Uri? LicenseUrl { get; init; }

    /// <summary>Gets the real file the test runs on, or null for a generated clip.</summary>
    public SpeedFile? File { get; init; }

    /// <summary>Gets a value indicating whether the source is interlaced.</summary>
    public bool Interlaced => Fixture?.Interlaced ?? File?.Video.Interlaced ?? false;

    /// <summary>Gets where in the source to start: a tenth of the way into a real file, past most intros.</summary>
    public TimeSpan StartAt => File is null ? TimeSpan.Zero : File.Duration / 10;

    /// <summary>Gets a value indicating whether the test only decodes.</summary>
    public bool DecodeOnly => OutputCodec is null;
}
