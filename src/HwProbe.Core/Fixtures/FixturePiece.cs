namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>A pinned part of a large WebM: its header and a run of whole clusters, which ffmpeg reads as a short file.</summary>
/// <param name="Url">The file.</param>
/// <param name="HeaderLength">Bytes before the first cluster: the EBML header, segment info and tracks.</param>
/// <param name="Start">The offset of the first cluster wanted.</param>
/// <param name="Length">The bytes of whole clusters from there.</param>
/// <param name="Sha256">The SHA-256 (lowercase hex) of the header and clusters together.</param>
public sealed record FixturePiece(Uri Url, long HeaderLength, long Start, long Length, string Sha256)
{
    /// <summary>Gets the bytes downloaded.</summary>
    public long Size => HeaderLength + Length;
}
