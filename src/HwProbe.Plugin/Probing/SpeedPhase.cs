namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Where a running speed run is.</summary>
public enum SpeedPhase
{
    /// <summary>Making or downloading its test videos.</summary>
    Preparing,

    /// <summary>Measuring.</summary>
    Measuring,

    /// <summary>A pause is asked for; the current measurement is finishing.</summary>
    Pausing,

    /// <summary>Paused between measurements.</summary>
    Paused,

    /// <summary>Cancelled; its ffmpeg runs are stopping.</summary>
    Cancelling,
}
