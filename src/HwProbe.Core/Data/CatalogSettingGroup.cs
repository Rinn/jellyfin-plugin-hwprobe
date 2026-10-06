namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>Settings that together pick one thing, such as the tone-mapping method, suggested as one table with a row per choice.</summary>
public sealed class CatalogSettingGroup
{
    /// <summary>Gets the key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the table's heading.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the option keys in the group.</summary>
    public required IReadOnlyList<string> Settings { get; init; }

    /// <summary>Gets the choices, in the order the table lists them; a run is the first whose conditions its settings meet.</summary>
    public required IReadOnlyList<CatalogGroupRow> Rows { get; init; }

    /// <summary>Gets a line shown under the table, or null.</summary>
    public string? Note { get; init; }
}
