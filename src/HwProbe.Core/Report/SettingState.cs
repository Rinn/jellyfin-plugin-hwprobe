namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>What the tests say about one option on Jellyfin's Transcoding page.</summary>
public enum SettingState
{
    /// <summary>The test passed, so the option can be turned on.</summary>
    TurnOn,

    /// <summary>The test failed, so the option should stay off.</summary>
    LeaveOff,

    /// <summary>No test covered the option.</summary>
    NotTested,
}
