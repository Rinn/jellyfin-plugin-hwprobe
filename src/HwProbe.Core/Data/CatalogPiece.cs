namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A pinned piece of a large WebM, as the catalog file writes it.</summary>
internal sealed class CatalogPiece
{
    /// <summary>Gets the file's URL.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the bytes before the first cluster.</summary>
    public required long HeaderLength { get; init; }

    /// <summary>Gets the offset of the first cluster wanted.</summary>
    public required long Start { get; init; }

    /// <summary>Gets the bytes of whole clusters from there.</summary>
    public required long Length { get; init; }

    /// <summary>Gets the SHA-256 (lowercase hex) of the header and clusters together.</summary>
    public required string Sha256 { get; init; }
}
