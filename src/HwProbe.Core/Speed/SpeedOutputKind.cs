namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What a speed test makes from its input.</summary>
public enum SpeedOutputKind
{
    /// <summary>A video codec at a quality, as a player asks for it.</summary>
    Transcode,

    /// <summary>Nothing: the video is decoded only.</summary>
    Decode,

    /// <summary>Trickplay images, which HwProbe 1.1.0 to 1.1.2 measured; kept so the runs they saved still load.</summary>
    Images,

    /// <summary>An audio codec, as a client asks for it.</summary>
    Audio,

    /// <summary>Nothing: the audio is decoded only.</summary>
    AudioDecode,
}
