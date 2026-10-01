using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>The real host.</summary>
public sealed class HostPlatform : IHostPlatform
{
    /// <inheritdoc/>
    public HostOs Os { get; } =
        OperatingSystem.IsLinux() ? HostOs.Linux
        : OperatingSystem.IsWindows() ? HostOs.Windows
        : OperatingSystem.IsMacOS() ? HostOs.MacOS
        : HostOs.Other;

    /// <inheritdoc/>
    public string OsDescription => RuntimeInformation.OSDescription;

    /// <inheritdoc/>
    public Version OsVersion => Environment.OSVersion.Version;

    /// <inheritdoc/>
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc/>
    public string? TryReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public DirectoryListing ListDirectory(string path, string pattern)
    {
        if (!Directory.Exists(path))
        {
            return new DirectoryListing(DirectoryAccess.Missing, []);
        }

        try
        {
            return new DirectoryListing(DirectoryAccess.Ok, [.. Directory.EnumerateFileSystemEntries(path, pattern)]);
        }
        catch (UnauthorizedAccessException)
        {
            return new DirectoryListing(DirectoryAccess.Denied, []);
        }
        catch (IOException)
        {
            return new DirectoryListing(DirectoryAccess.Denied, []);
        }
    }
}
