namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>The last step a run executes.</summary>
public enum StopStage
{
    /// <summary>Build enumeration only.</summary>
    Build,

    /// <summary>Through device open, the smoke probe and tier resolution.</summary>
    Devices,

    /// <summary>Through the full codec matrix.</summary>
    Matrix,
}
