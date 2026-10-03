namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>What a request to delete saved results did.</summary>
public enum DeleteOutcome
{
    /// <summary>Deleted.</summary>
    Deleted,

    /// <summary>Nothing by that ID.</summary>
    NotFound,

    /// <summary>Refused: a probe or speed run is running.</summary>
    Busy,
}
