namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>What the runner is doing, or last did.</summary>
public enum ProbeActivity
{
    /// <summary>Testing which options work.</summary>
    Probe,

    /// <summary>Measuring how fast the working options are.</summary>
    Speed,
}
