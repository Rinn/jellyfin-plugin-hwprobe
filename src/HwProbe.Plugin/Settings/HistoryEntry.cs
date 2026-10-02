namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>One write to the encoding settings.</summary>
/// <param name="TimeUtc">When it happened.</param>
/// <param name="User">The admin who made it.</param>
/// <param name="Kind">What it was.</param>
/// <param name="Changes">The values changed.</param>
public sealed record HistoryEntry(DateTimeOffset TimeUtc, string User, HistoryKind Kind, IReadOnlyList<AppliedChange> Changes)
{
    /// <summary>Gets when this entry was reverted, or null.</summary>
    public DateTimeOffset? RevertedUtc { get; init; }
}
