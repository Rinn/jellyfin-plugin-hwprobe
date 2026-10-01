namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Whether a probe is in progress.</summary>
public enum ProbeState
{
    /// <summary>No probe running.</summary>
    Idle,

    /// <summary>A probe is running.</summary>
    Running,
}
