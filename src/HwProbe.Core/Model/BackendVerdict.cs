namespace Jellyfin.Plugin.HwProbe.Core.Model;

/// <summary>Result per (backend, device) after the device open and smoke probe.</summary>
public enum BackendVerdict
{
    /// <summary>Device opens and the H.264 smoke probe passes.</summary>
    Viable,

    /// <summary>No device to open.</summary>
    NotPresent,

    /// <summary>The device exists but this user cannot open it.</summary>
    PermissionDenied,

    /// <summary>The device opens but the smoke probe fails.</summary>
    DevicePresentPipelineBroken,

    /// <summary>The ffmpeg build lacks this backend.</summary>
    NotBuilt,

    /// <summary>No hardware was available to validate on.</summary>
    Untested,
}
