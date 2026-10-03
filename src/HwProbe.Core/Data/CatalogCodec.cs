namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A video codec Jellyfin transcodes to.</summary>
public sealed class CatalogCodec
{
    /// <summary>Gets the codec as Jellyfin names it, e.g. <c>hevc</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the page calls it, e.g. <c>HEVC</c>.</summary>
    public required string Name { get; init; }
}
