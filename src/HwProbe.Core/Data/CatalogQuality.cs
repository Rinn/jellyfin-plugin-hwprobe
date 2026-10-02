namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A quality a player offers: a video bitrate.</summary>
public sealed class CatalogQuality
{
    /// <summary>Gets the key in output keys, e.g. <c>8mbps</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the player calls it, e.g. <c>8 Mbps</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the video bitrate.</summary>
    public required int Bitrate { get; init; }
}
