namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What a speed run makes from each video: a transcode a client asks for, or decoding alone.</summary>
/// <param name="Key">Stable key, e.g. <c>720p-h264</c>.</param>
/// <param name="Label">What the page calls it, e.g. <c>720p H.264, 4 Mbps</c>.</param>
/// <param name="Codec">The output codec, or null to decode only.</param>
/// <param name="Height">The output height the client asks for.</param>
/// <param name="Bitrate">The video bitrate the client asks for.</param>
/// <param name="BitrateRange">jellyfin-web's lowest and highest bitrate for the height, for the bitrate comparison.</param>
public sealed record SpeedOutput(string Key, string Label, string? Codec, int Height, int Bitrate, (int Low, int High) BitrateRange)
{
    /// <summary>Gets what's made, for the page, e.g. <c>Video re-encoded to 720p, audio to stereo AAC</c>.</summary>
    public string Detail { get; init; } = string.Empty;
}
