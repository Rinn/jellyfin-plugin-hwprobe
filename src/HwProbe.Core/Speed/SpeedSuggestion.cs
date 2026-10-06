namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>One suggestion drawn from measured results.</summary>
/// <param name="Kind">What it's about.</param>
/// <param name="Outputs">The outputs it rests on, as the results label them.</param>
public sealed record SpeedSuggestion(SpeedSuggestionKind Kind, IReadOnlyList<string> Outputs)
{
    /// <summary>Gets the backend suggested, or the configured one that falls behind.</summary>
    public Model.HwType? Type { get; init; }

    /// <summary>Gets that backend's device, or empty.</summary>
    public string Device { get; init; } = string.Empty;

    /// <summary>Gets the catalog option key, e.g. <c>EncoderPreset</c>, for a setting suggestion.</summary>
    public string? Setting { get; init; }

    /// <summary>Gets the value suggested, as the catalog keys it.</summary>
    public string? Value { get; init; }

    /// <summary>Gets the values it was compared with.</summary>
    public IReadOnlyList<string> Others { get; init; } = [];

    /// <summary>Gets how much faster the suggestion measured, as a fraction; negative when slower.</summary>
    public double? Gain { get; init; }

    /// <summary>Gets the slowest measured speed with the suggestion, as a multiple of real time.</summary>
    public double? Speed { get; init; }

    /// <summary>Gets a value indicating whether the value gives a worse picture than the values it was compared with, e.g. a faster preset or a bitrate limit.</summary>
    public bool LowerQuality { get; init; }

    /// <summary>Gets a value indicating whether a suggested backend measured alike with the configured one and is suggested as the one Jellyfin prefers, QSV over VAAPI.</summary>
    public bool Preferred { get; init; }

    /// <summary>Gets how much more efficient the suggestion is where it measured alike, each at its smallest saving across the outputs; empty otherwise.</summary>
    public IReadOnlyList<ResourceSaving> Savings { get; init; } = [];

    /// <summary>Gets a value indicating whether the value is the server's current one, so it confirms the setting rather than suggesting a change.</summary>
    public bool Current { get; init; }

    /// <summary>Gets the fewest concurrent streams kept with the value across the outputs, when counted.</summary>
    public int? Streams { get; init; }

    /// <summary>Gets the fewest concurrent streams kept with the values it was compared with, when counted.</summary>
    public int? OtherStreams { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Streams"/> hit the count's cap, so it's a lower bound.</summary>
    public bool StreamsCapped { get; init; }

    /// <summary>Gets a value indicating whether <see cref="OtherStreams"/> hit the count's cap, so it's a lower bound.</summary>
    public bool OtherStreamsCapped { get; init; }

    /// <summary>Gets the key of the catalog setting group the setting is in, or null, so the page shows the group's suggestions as one table.</summary>
    public string? Group { get; init; }

    /// <summary>Gets the choice the runs with the value made in that group, as the catalog labels it, or null.</summary>
    public string? Row { get; init; }

    /// <summary>Gets the values it was compared with, each with its own speed and streams, for the page to show beside it; empty when nothing was.</summary>
    public IReadOnlyList<SpeedComparedValue> Compared { get; init; } = [];

    /// <summary>Gets a value indicating whether it rests on generated test videos alone, which encode faster than real video.</summary>
    public bool TestVideosOnly { get; init; }
}
