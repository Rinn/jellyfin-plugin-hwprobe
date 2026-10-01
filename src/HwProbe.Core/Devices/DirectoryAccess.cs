namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>Whether a directory could be listed.</summary>
public enum DirectoryAccess
{
    /// <summary>Listed successfully.</summary>
    Ok,

    /// <summary>The directory does not exist.</summary>
    Missing,

    /// <summary>The directory exists but could not be read.</summary>
    Denied,
}
