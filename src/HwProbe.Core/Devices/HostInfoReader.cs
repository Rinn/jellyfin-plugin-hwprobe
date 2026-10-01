namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>Reads OS, kernel and container facts.</summary>
public sealed class HostInfoReader
{
    private readonly IHostPlatform _platform;

    /// <summary>Initializes a new instance of the <see cref="HostInfoReader"/> class.</summary>
    /// <param name="platform">Host access.</param>
    public HostInfoReader(IHostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
    }

    /// <summary>Reads the host facts.</summary>
    /// <returns>The host info; unreadable fields are <c>unknown</c>, never an exception.</returns>
    public HostInfo Read() => new(_platform.Os, ReadKernel(), _platform.Os == HostOs.Linux ? DetectContainer() : null);

    /// <summary>Reads the kernel release, the equivalent of <c>uname -r</c>.</summary>
    /// <returns>The release, or <c>unknown</c>.</returns>
    private string ReadKernel()
    {
        switch (_platform.Os)
        {
            case HostOs.Linux:
                var release = _platform.TryReadText("/proc/sys/kernel/osrelease")?.Trim();
                return string.IsNullOrEmpty(release) ? DeviceEnumerator.Unknown : release;
            case HostOs.MacOS:
                // .NET 10 reports "macOS 27.0.1"; older runtimes reported "Darwin <release> …".
                var parts = _platform.OsDescription.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts switch
                {
                    ["Darwin", var darwin, ..] => darwin,
                    ["macOS", var version, ..] => $"macOS {version}",
                    _ => DeviceEnumerator.Unknown,
                };
            default:
                return _platform.OsVersion.ToString();
        }
    }

    /// <summary>Detects a container runtime from marker files, then PID 1's cgroup.</summary>
    /// <returns>The runtime name, or null when none is detected.</returns>
    private string? DetectContainer()
    {
        if (_platform.FileExists("/.dockerenv"))
        {
            return "docker";
        }

        if (_platform.FileExists("/run/.containerenv"))
        {
            return "podman";
        }

        var cgroup = _platform.TryReadText("/proc/1/cgroup") ?? string.Empty;
        string[] markers = ["kubepods", "docker", "containerd", "lxc"];
        var marker = markers.FirstOrDefault(m => cgroup.Contains(m, StringComparison.Ordinal));
        return marker == "kubepods" ? "kubernetes" : marker;
    }
}
