namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>A value a suggestion was compared with, as measured on the same outputs.</summary>
/// <param name="Value">The value, as the catalog keys it; for a backend suggestion, the backend type.</param>
/// <param name="Speed">The slowest measured speed with it, as a multiple of real time.</param>
/// <param name="Streams">The fewest concurrent streams kept with it, when counted.</param>
/// <param name="StreamsCapped">Whether that count hit its cap, so it's a lower bound.</param>
public sealed record SpeedComparedValue(string Value, double Speed, int? Streams, bool StreamsCapped)
{
    /// <summary>Gets the choice the runs with it made in the setting's group, as the catalog labels it, or null when the setting isn't in one.</summary>
    public string? Row { get; init; }
}
