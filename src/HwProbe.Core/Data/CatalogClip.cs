namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A clip a speed run reads, as the catalog file writes it.</summary>
internal sealed class CatalogClip
{
    /// <summary>Gets the cached file name.</summary>
    public required string File { get; init; }

    /// <summary>Gets how long the clip is, for the page, or null when it isn't a video.</summary>
    public double? Seconds { get; init; }

    /// <summary>Gets the codec as Jellyfin reports it.</summary>
    public required string Codec { get; init; }

    /// <summary>Gets the luma bit depth.</summary>
    public int BitDepth { get; init; } = 8;

    /// <summary>Gets a value indicating whether the clip is HDR10.</summary>
    public bool Hdr10 { get; init; }

    /// <summary>Gets a value indicating whether the clip is interlaced.</summary>
    public bool Interlaced { get; init; }

    /// <summary>Gets the software encoder that makes it, or null for a download.</summary>
    public string? Encoder { get; init; }

    /// <summary>Gets the ffmpeg arguments before the output path, with <c>{name}</c> placeholders.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Gets the piece to download and encode from, or null.</summary>
    public CatalogPiece? Piece { get; init; }

    /// <summary>Gets the URL to download the clip itself from, or null.</summary>
    public string? Download { get; init; }

    /// <summary>Gets the SHA-256 a downloaded clip must match, or null.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Gets a longer time limit for making the clip, in minutes, or null.</summary>
    public int? GenerateMinutes { get; init; }

    /// <summary>Gets the pixel format Jellyfin reports when it isn't 4:2:0 at the bit depth, or null.</summary>
    public string? PixelFormat { get; init; }

    /// <summary>Gets the profile Jellyfin reports, e.g. <c>Professional</c>, or null.</summary>
    public string? Profile { get; init; }

    /// <summary>Gets the audio codec as Jellyfin reports it, or null for 5.1 AAC.</summary>
    public string? AudioCodec { get; init; }

    /// <summary>Gets the audio channel count, or null for 5.1 AAC's six.</summary>
    public int? AudioChannels { get; init; }

    /// <summary>Gets a value indicating whether the clip is kept across ffmpeg builds.</summary>
    public bool KeepAcrossBuilds { get; init; }
}
