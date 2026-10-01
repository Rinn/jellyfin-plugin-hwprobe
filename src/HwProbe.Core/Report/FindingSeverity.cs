namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>How much a finding matters.</summary>
public enum FindingSeverity
{
    /// <summary>Informational.</summary>
    Info,

    /// <summary>Works, but worse than this host could do.</summary>
    Warn,

    /// <summary>Something that should work does not.</summary>
    Error,
}
