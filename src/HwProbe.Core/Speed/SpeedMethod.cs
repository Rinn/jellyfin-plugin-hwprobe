namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>How the number of real-time streams is found.</summary>
public enum SpeedMethod
{
    /// <summary>From one transcode's fps alone.</summary>
    Quick,

    /// <summary>From one transcode's fps, then confirmed by running that many at once and searching down when they fall behind.</summary>
    Confirm,

    /// <summary>By running 1, 2, 4, 8 and 16 at once until they fall behind, then searching between.</summary>
    Full,
}
