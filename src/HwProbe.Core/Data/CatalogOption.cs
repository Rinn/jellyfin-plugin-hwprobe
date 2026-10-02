namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>One choice in a select.</summary>
public sealed class CatalogOption
{
    /// <summary>Gets the value sent with the request, or null for none.</summary>
    public int? Value { get; init; }

    /// <summary>Gets what the select shows.</summary>
    public required string Label { get; init; }
}
