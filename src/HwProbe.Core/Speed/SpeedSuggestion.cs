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

    /// <summary>Gets a value indicating whether the faster value gives a worse picture, e.g. a faster preset.</summary>
    public bool LowerQuality { get; init; }

    /// <summary>Gets a value indicating whether it rests on generated test videos alone, which encode faster than real video.</summary>
    public bool TestVideosOnly { get; init; }
}
