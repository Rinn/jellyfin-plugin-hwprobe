namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What a speed run makes from each video: a codec at a quality, as a player asks for it, images at an interval, or decoding alone.</summary>
/// <param name="Key">Stable key, e.g. <c>hevc-8mbps</c>.</param>
/// <param name="Label">What the page calls it, e.g. <c>HEVC, 8 Mbps</c>.</param>
/// <param name="Codec">The output codec, or null to decode only.</param>
/// <param name="Bitrate">The video bitrate asked for; Jellyfin picks the size from it.</param>
public sealed record SpeedOutput(string Key, string Label, string? Codec, int Bitrate)
{
    /// <summary>Gets what it makes.</summary>
    public SpeedOutputKind Kind { get; init; } = Codec is null ? SpeedOutputKind.Decode : SpeedOutputKind.Transcode;
}
