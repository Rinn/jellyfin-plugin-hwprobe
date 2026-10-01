namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Per-user cache location for fixtures and reports.</summary>
internal static class CacheDirectory
{
    /// <summary>Returns <c>&lt;cache&gt;/hwprobe</c> for the current OS.</summary>
    /// <returns>The absolute directory path; not created.</returns>
    public static string Resolve()
    {
        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches");
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            root = string.IsNullOrEmpty(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
                : xdg;
        }

        return Path.Combine(root, "hwprobe");
    }
}
