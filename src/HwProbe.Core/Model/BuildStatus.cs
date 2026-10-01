namespace Jellyfin.Plugin.HwProbe.Core.Model;

/// <summary>Per-backend build status: what the binary was compiled with, not what works.</summary>
public enum BuildStatus
{
    /// <summary>The build includes this backend, so Jellyfin offers it.</summary>
    Selectable,

    /// <summary>The build lacks this backend.</summary>
    NotBuilt,
}
