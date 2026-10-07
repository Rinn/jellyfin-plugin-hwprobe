namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What a speed test makes from its input.</summary>
public enum SpeedOutputKind
{
    /// <summary>A video codec at a quality, as a player asks for it.</summary>
    Transcode,

    /// <summary>Nothing: the video is decoded only.</summary>
    Decode,

    /// <summary>Images at an interval, as Jellyfin extracts them for trickplay.</summary>
    Images,
}
