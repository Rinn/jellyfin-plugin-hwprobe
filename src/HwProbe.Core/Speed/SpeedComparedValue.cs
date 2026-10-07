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

    /// <summary>Gets its slowest speed on each output, for the page's column per output; empty for a backend.</summary>
    public IReadOnlyList<OutputSpeed> Speeds { get; init; } = [];

    /// <summary>Gets a value indicating whether it's the server's current value; set for a setting outside a group, as a group's table and a backend's work that out themselves.</summary>
    public bool Current { get; init; }

    /// <summary>Gets whether it gives a worse picture than the suggested value; null where the setting doesn't trade speed for quality or isn't compared that way.</summary>
    public bool? LowerQuality { get; init; }
}
