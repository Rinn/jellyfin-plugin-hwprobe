namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>How a settings write ended.</summary>
public enum ApplyOutcome
{
    /// <summary>The changes were saved; the list may be empty when nothing differed.</summary>
    Applied,

    /// <summary>The request didn't match the latest report.</summary>
    Rejected,

    /// <summary>A probe or another write is running.</summary>
    Busy,

    /// <summary>No probe has completed yet.</summary>
    NoReport,

    /// <summary>There is no change left to revert.</summary>
    NothingToRevert,
}
