namespace Jellyfin.Plugin.HwProbe.Core.Model;

/// <summary>The result of one probe cell.</summary>
public enum ProbeOutcome
{
    /// <summary>Exit 0, frames produced, and the backend's device-init confirmation present.</summary>
    Pass,

    /// <summary>The device could not be opened.</summary>
    DeviceUnavailable,

    /// <summary>The device exists but access was denied.</summary>
    PermissionDenied,

    /// <summary>The codec is not supported by the device or build.</summary>
    CodecUnsupported,

    /// <summary>A filter in the chain is unsupported.</summary>
    FilterUnsupported,

    /// <summary>The hard timeout fired.</summary>
    Timeout,

    /// <summary>ffmpeg exited cleanly but without using the hardware.</summary>
    SoftwareFallback,

    /// <summary>A prerequisite was missing (fixture encoder, pruned column); not a failure.</summary>
    Skipped,

    /// <summary>No hardware or fixture available to validate this cell; not a failure.</summary>
    Untested,
}
