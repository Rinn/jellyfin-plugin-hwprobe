namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>What a performance test does when the server starts transcoding for a player.</summary>
public enum TranscodeAction
{
    /// <summary>Wait while it transcodes, and measure an interrupted measurement again.</summary>
    Pause,

    /// <summary>Keep measuring alongside it.</summary>
    Continue,

    /// <summary>Stop the run, keeping the measurements finished before then.</summary>
    Cancel,
}
