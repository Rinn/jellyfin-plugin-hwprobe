namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>Which Intel low-power (VDEnc) encoders a GPU has, by its graphics generation.</summary>
public enum LowPowerSupport
{
    /// <summary>Not known: not Intel, an unreadable ID, or Gen 10 and newer, where both can exist.</summary>
    Unknown,

    /// <summary>Gen 8 and older: no low-power encoders.</summary>
    None,

    /// <summary>Gen 9.x: low-power H.264 only.</summary>
    H264Only,
}
