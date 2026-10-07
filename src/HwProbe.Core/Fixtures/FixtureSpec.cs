namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>One fixture clip: how to generate it and the stream properties a synthetic job needs.</summary>
/// <param name="FileName">Cached file name; its extension picks the container.</param>
/// <param name="Codec">Codec name as Jellyfin's <c>MediaStream.Codec</c> reports it.</param>
/// <param name="BitDepth">Luma bit depth.</param>
/// <param name="IsHdr10">Whether the clip carries BT.2020 PQ (HDR10) metadata.</param>
/// <param name="RequiredEncoder">Software encoder that must be in the build, or null when none can make it.</param>
/// <param name="EncodeArguments">ffmpeg arguments before the output path.</param>
/// <param name="UntestedReason">Why the fixture can never be generated, or null when it can.</param>
public sealed record FixtureSpec(
    string FileName,
    string Codec,
    int BitDepth,
    bool IsHdr10,
    string? RequiredEncoder,
    string EncodeArguments,
    string? UntestedReason)
{
    /// <summary>Gets the pixel format Jellyfin's <c>MediaStream.PixelFormat</c> reports, or null for 4:2:0 at <see cref="BitDepth"/>.</summary>
    public string? PixelFormat { get; init; }

    /// <summary>Gets the profile Jellyfin's <c>MediaStream.Profile</c> reports, e.g. <c>Rext</c>, or null.</summary>
    public string? Profile { get; init; }

    /// <summary>Gets a value indicating whether a copy ships in this assembly, used when the clip can't be generated.</summary>
    /// <remarks>Made with <see cref="EncodeArguments"/> by scripts/make-bundled-fixtures.sh and pinned by <see cref="Sha256"/>.</remarks>
    public bool Bundled { get; init; }

    /// <summary>Gets a report key to use instead of the one built from codec, bit depth and profile, or null.</summary>
    public string? Key { get; init; }

    /// <summary>Gets a value indicating whether the clip is interlaced.</summary>
    public bool Interlaced { get; init; }

    /// <summary>Gets where to download the clip when it can't be generated, or null.</summary>
    public Uri? DownloadUrl { get; init; }

    /// <summary>Gets the pinned SHA-256 (lowercase hex) a downloaded clip must match.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Gets arguments to retry with when the first encode fails, or null.</summary>
    public string? FallbackArguments { get; init; }

    /// <summary>Gets a longer time limit for generating this clip, or null for the builder's.</summary>
    public TimeSpan? GenerateTimeout { get; init; }

    /// <summary>Gets a value indicating whether the clip is kept across ffmpeg builds, for downloads too large to repeat.</summary>
    public bool KeepAcrossBuilds { get; init; }

    /// <summary>Gets a pinned piece of a large file to download and check first; <see cref="EncodeArguments"/> reads it as <c>{piece}</c>, or the piece is the clip when there are none.</summary>
    public FixturePiece? Piece { get; init; }

    /// <summary>Gets how long the clip plays, or null when not known; a downloaded piece's own header gives the whole film's length.</summary>
    public double? Seconds { get; init; }

    /// <summary>Gets the audio codec as Jellyfin reports it, or null for 5.1 AAC.</summary>
    public string? AudioCodec { get; init; }

    /// <summary>Gets the audio channel count, or null for 5.1 AAC's six.</summary>
    public int? AudioChannels { get; init; }
}
