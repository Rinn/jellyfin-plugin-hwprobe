namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What a suggestion from measured results is about.</summary>
public enum SpeedSuggestionKind
{
    /// <summary>Another hardware backend was faster than the configured one.</summary>
    FastestBackend,

    /// <summary>The configured backend fell behind real time.</summary>
    FallsBehind,

    /// <summary>A setting's other value measured faster.</summary>
    FasterSetting,

    /// <summary>A setting's higher-quality value still keeps up with real time.</summary>
    HigherQuality,

    /// <summary>A setting's other value measured as fast and is more efficient: less CPU, memory, or GPU.</summary>
    EfficientSetting,

    /// <summary>Nothing compared with the server's value is worth changing to, so it's kept.</summary>
    NoChange,
}
