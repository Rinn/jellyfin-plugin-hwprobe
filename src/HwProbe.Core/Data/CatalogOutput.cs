namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>The decode-only output, as the catalog file writes it.</summary>
internal sealed class CatalogOutput
{
    /// <summary>Gets the stable key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the page calls it.</summary>
    public required string Label { get; init; }
}
