using Jellyfin.Plugin.HwProbe.Core.Devices;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Devices;

/// <summary>An in-memory host: files with contents, and directories that may be unreadable.</summary>
internal sealed class FakeHostPlatform : IHostPlatform
{
    /// <summary>Initializes a new instance of the <see cref="FakeHostPlatform"/> class.</summary>
    /// <param name="os">The OS family to report.</param>
    public FakeHostPlatform(HostOs os)
    {
        Os = os;
    }

    /// <inheritdoc/>
    public HostOs Os { get; }

    /// <inheritdoc/>
    public string OsDescription { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Version OsVersion { get; init; } = new(0, 0);

    /// <inheritdoc/>
    public string Architecture { get; init; } = "x64";

    /// <summary>Gets file contents by path; a null value means the file exists but is unreadable.</summary>
    public Dictionary<string, string?> Files { get; } = [];

    /// <summary>Gets directories that exist but cannot be listed.</summary>
    public HashSet<string> DeniedDirectories { get; } = [];

    /// <summary>Gets files whose open is refused for lack of permission.</summary>
    public HashSet<string> DeniedFiles { get; } = [];

    /// <inheritdoc/>
    public bool FileExists(string path) => Files.ContainsKey(path);

    /// <inheritdoc/>
    public bool IsAccessDenied(string path) => DeniedFiles.Contains(path);

    /// <inheritdoc/>
    public string? TryReadText(string path) => Files.GetValueOrDefault(path);

    /// <inheritdoc/>
    public DirectoryListing ListDirectory(string path, string pattern)
    {
        if (DeniedDirectories.Contains(path))
        {
            return new DirectoryListing(DirectoryAccess.Denied, []);
        }

        var prefix = path + "/";
        var stem = pattern.TrimEnd('*');
        List<string> entries = [.. Files.Keys.Where(f => f.StartsWith(prefix + stem, StringComparison.Ordinal) && !f[prefix.Length..].Contains('/', StringComparison.Ordinal))];
        return entries.Count == 0 && !Files.Keys.Any(f => f.StartsWith(prefix, StringComparison.Ordinal))
            ? new DirectoryListing(DirectoryAccess.Missing, [])
            : new DirectoryListing(DirectoryAccess.Ok, entries);
    }
}
