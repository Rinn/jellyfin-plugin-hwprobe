namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A speed variation as the page lists it.</summary>
public sealed class CatalogVariation
{
    /// <summary>Gets the <see cref="Speed.SpeedComparison"/> it measures, by name; a string, since Jellyfin writes a flags enum as a list.</summary>
    public required string Key { get; init; }

    /// <summary>Gets Jellyfin's name for the setting.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the values measured beside the server's.</summary>
    public required string Measured { get; init; }
}
