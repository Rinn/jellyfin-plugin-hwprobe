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
    public string Architecture => RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.Arm => "arm",
        System.Runtime.InteropServices.Architecture.Armv6 => "armv6",
        var other => other.ToString(),
    };

    /// <inheritdoc/>
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc/>
    public bool IsAccessDenied(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

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
