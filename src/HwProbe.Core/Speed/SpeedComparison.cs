namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Optional runs that change one Jellyfin setting from the base, each shown beside the base result.</summary>
[Flags]
public enum SpeedComparison
{
    /// <summary>No comparisons.</summary>
    None = 0,

    /// <summary>"Enable VBR audio encoding" the other way.</summary>
    AudioVbr = 1,

    /// <summary>The encoder preset one step faster and one slower.</summary>
    Preset = 2,

    /// <summary>The software encoders' CRF at 18 and 28.</summary>
    Quality = 4,

    /// <summary>jellyfin-web's lowest and highest bitrate for the output height.</summary>
    Bitrate = 8,

    /// <summary>Double-rate deinterlacing, and BWDIF instead of YADIF.</summary>
    Deinterlace = 16,

    /// <summary>Intel low-power encoding, VPP tone-mapping, and the QSV and NVIDIA decoder choices.</summary>
    Paths = 32,

    /// <summary>Text (ASS) and image (PGS) subtitles burned into the picture.</summary>
    Subtitles = 64,
}
