namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>The result of a settings write.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Changes">The values changed.</param>
/// <param name="Reason">Why it was refused, or which reverts were skipped; null when there's nothing to say.</param>
public sealed record ApplyResult(ApplyOutcome Outcome, IReadOnlyList<AppliedChange> Changes, string? Reason)
{
    /// <summary>Gets a value indicating whether Jellyfin must restart for the change to take full effect.</summary>
    public bool RestartRequired { get; init; }
}
