namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>One output measured with two values of a setting, from two runs that differ in that setting alone.</summary>
/// <param name="Key">The catalog option key.</param>
/// <param name="Value">The value, as the catalog keys it.</param>
/// <param name="Other">The value it was compared with.</param>
/// <param name="Test">The test key.</param>
/// <param name="Label">The output as the results label it.</param>
/// <param name="Gain">How much faster the value measured, as a fraction.</param>
/// <param name="Speed">The speed with the value, as a multiple of real time.</param>
/// <param name="Generated">Whether the input is a generated test video.</param>
/// <param name="Savings">What the value used less of.</param>
/// <param name="Streams">The concurrent streams kept with the value, when counted.</param>
/// <param name="OtherStreams">The concurrent streams kept with the other value, when counted.</param>
/// <param name="ToneMaps">Whether the value's run tone maps.</param>
/// <param name="Capped">Whether <paramref name="Streams"/> hit its cap.</param>
/// <param name="OtherCapped">Whether <paramref name="OtherStreams"/> hit its cap.</param>
/// <param name="OtherSpeed">The speed with the other value.</param>
/// <param name="Row">The choice the value's run made in the setting's group, or null.</param>
/// <param name="OtherRow">The choice the other value's run made in the setting's group, or null.</param>
/// <param name="Video">The input video's title, or null.</param>
/// <param name="Output">The output's own label, or null.</param>
internal sealed record SettingComparison(
    string Key,
    string Value,
    string Other,
    string Test,
    string Label,
    double Gain,
    double Speed,
    bool Generated,
    IReadOnlyList<ResourceSaving> Savings,
    int? Streams,
    int? OtherStreams,
    bool ToneMaps,
    bool Capped,
    bool OtherCapped,
    double OtherSpeed,
    string? Row,
    string? OtherRow,
    string? Video,
    string? Output)
{
    /// <summary>Gets the value's speed on the output.</summary>
    public OutputSpeed Measured => new(Label, Video, Output, Speed, Streams, Capped);

    /// <summary>Gets the other value's speed on the output.</summary>
    public OutputSpeed OtherMeasured => new(Label, Video, Output, OtherSpeed, OtherStreams, OtherCapped);
}
