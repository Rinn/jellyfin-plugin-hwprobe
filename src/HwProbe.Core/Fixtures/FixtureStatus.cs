namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Whether a fixture is ready for probes to decode.</summary>
public enum FixtureStatus
{
    /// <summary>Generated or cached, and verified against its manifest.</summary>
    Available,

    /// <summary>The build lacks the software encoder; dependent cells are Skipped, not failed.</summary>
    Skipped,

    /// <summary>Generation was attempted and failed.</summary>
    Failed,

    /// <summary>The fixture can never be generated; dependent cells are Untested.</summary>
    Untested,
}
