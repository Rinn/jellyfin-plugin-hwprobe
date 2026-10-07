namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>An output that isn't a codec at a quality (decoding alone, images), as the catalog file writes it.</summary>
internal sealed class CatalogOutput
{
    /// <summary>Gets the stable key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the page calls it.</summary>
    public required string Label { get; init; }
}
