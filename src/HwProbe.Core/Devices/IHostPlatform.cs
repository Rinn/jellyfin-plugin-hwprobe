namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>Host OS and filesystem access, injectable so per-OS branches are testable anywhere.</summary>
public interface IHostPlatform
{
    /// <summary>Gets the operating system family.</summary>
    HostOs Os { get; }

    /// <summary>Gets the runtime's OS description, e.g. <c>Darwin 25.0.0 Darwin Kernel Version …</c>.</summary>
    string OsDescription { get; }

    /// <summary>Gets the OS version as reported by the runtime.</summary>
    Version OsVersion { get; }

    /// <summary>Reports whether a file exists.</summary>
    /// <param name="path">Absolute path.</param>
    /// <returns>True if the file exists.</returns>
    bool FileExists(string path);

    /// <summary>Reads a text file.</summary>
    /// <param name="path">Absolute path.</param>
    /// <returns>The contents, or null when missing or unreadable.</returns>
    string? TryReadText(string path);

    /// <summary>Lists entries in a directory matching a glob pattern.</summary>
    /// <param name="path">Absolute directory path.</param>
    /// <param name="pattern">A <see cref="Directory.EnumerateFileSystemEntries(string, string)"/> pattern.</param>
    /// <returns>The listing; never throws for a missing or unreadable directory.</returns>
    DirectoryListing ListDirectory(string path, string pattern);
}
