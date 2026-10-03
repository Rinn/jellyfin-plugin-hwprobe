using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A speed accuracy as the page lists it.</summary>
public sealed class CatalogMethod
{
    /// <summary>Gets the method.</summary>
    public required SpeedMethod Key { get; init; }

    /// <summary>Gets its short name, e.g. <c>Standard</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what it does, for the select.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the low and high seconds one measurement takes, for the page's estimate.</summary>
    public required IReadOnlyList<int> Seconds { get; init; }
}
