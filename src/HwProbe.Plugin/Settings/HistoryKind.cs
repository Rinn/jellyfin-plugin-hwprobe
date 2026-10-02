namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>What a history entry records.</summary>
public enum HistoryKind
{
    /// <summary>Options applied from the advice.</summary>
    Apply,

    /// <summary>The hardware acceleration backend and device switched.</summary>
    Backend,

    /// <summary>An earlier entry undone.</summary>
    Revert,
}
