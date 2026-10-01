namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>The host operating system family.</summary>
public enum HostOs
{
    /// <summary>Linux.</summary>
    Linux,

    /// <summary>Windows.</summary>
    Windows,

    /// <summary>macOS.</summary>
    MacOS,

    /// <summary>Anything else.</summary>
    Other,
}
