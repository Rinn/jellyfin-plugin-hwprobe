namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>One choice in a <see cref="CatalogSettingGroup"/>: what a run with these settings, on these backends, used.</summary>
public sealed class CatalogGroupRow
{
    /// <summary>Gets the row's label.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the backends it applies on, or null for every backend.</summary>
    public IReadOnlyList<Model.HwType>? Backends { get; init; }

    /// <summary>Gets the option whose description the row shows on hover, or null.</summary>
    public string? Describes { get; init; }

    /// <summary>Gets a warning shown under the table when the row is in it, or null.</summary>
    public string? Caveat { get; init; }

    /// <summary>Gets the values the run's settings must have, by option key, or null for any.</summary>
    public IReadOnlyDictionary<string, string>? When { get; init; }
}
