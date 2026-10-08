namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What a suggestion from measured results is about.</summary>
public enum SpeedSuggestionKind
{
    /// <summary>Another hardware backend was faster than the configured one.</summary>
    FastestBackend,

    /// <summary>The configured backend fell behind real time.</summary>
    FallsBehind,

    /// <summary>None of two or more backends measured kept real time on an output; the fastest one is given.</summary>
    TooSlowEverywhere,

    /// <summary>A setting's other value measured faster.</summary>
    FasterSetting,

    /// <summary>A setting's higher-quality value still keeps up with real time.</summary>
    HigherQuality,

    /// <summary>The value that avoids a known drawback of the value suggested beside it: VBR audio off, or the Internet streaming bitrate limit that keeps real time.</summary>
    Compatible,

    /// <summary>A setting's other value measured as fast and is more efficient: less CPU, memory, or GPU.</summary>
    EfficientSetting,

    /// <summary>No Internet streaming bitrate limit, when higher qualities fall behind on the configured backend: a limit transcodes videos that could direct play.</summary>
    BitrateLimit,

    /// <summary>Nothing compared with the server's value is worth changing to, so it's kept.</summary>
    NoChange,

    /// <summary>The value the catalog recommends for a setting whatever the measurements, such as Auto for the encoding preset.</summary>
    RecommendedValue,
}
