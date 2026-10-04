namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>A report-level observation with a remedy.</summary>
/// <param name="Severity">How much it matters.</param>
/// <param name="Code">Stable kebab-case identifier, e.g. <c>legacy-copyback</c>.</param>
/// <param name="Message">Human-readable explanation and remedy.</param>
public sealed record Finding(FindingSeverity Severity, string Code, string Message)
{
    /// <summary>Gets the backend it's about, or null for the whole server; the page groups by it rather than reading the message.</summary>
    public Model.HwType? Backend { get; init; }

    /// <summary>Gets a short fix, when the user can act on the finding.</summary>
    public Fix? Fix { get; init; }
}
