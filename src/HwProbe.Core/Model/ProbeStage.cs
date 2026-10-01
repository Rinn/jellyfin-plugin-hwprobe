namespace Jellyfin.Plugin.HwProbe.Core.Model;

/// <summary>Which step of a run a probe belongs to.</summary>
public enum ProbeStage
{
    /// <summary>Enumerating what the ffmpeg build supports; no device access.</summary>
    Build,

    /// <summary>Opening a device with no input or output.</summary>
    DeviceOpen,

    /// <summary>A short H.264 transcode through the device.</summary>
    Smoke,

    /// <summary>Resolving the filter-pipeline tier.</summary>
    Tier,

    /// <summary>One cell of the codec matrix.</summary>
    Matrix,
}
