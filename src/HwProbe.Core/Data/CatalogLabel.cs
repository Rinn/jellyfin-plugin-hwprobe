namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>One choice in a select, keyed by name.</summary>
public sealed class CatalogLabel
{
    /// <summary>Gets the value sent with the request.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the select shows.</summary>
    public required string Label { get; init; }
}
