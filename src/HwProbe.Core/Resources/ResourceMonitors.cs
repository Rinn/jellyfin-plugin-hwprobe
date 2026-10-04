using System.Diagnostics;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Picks the platform's way of following a process.</summary>
public static class ResourceMonitors
{
    /// <summary>Starts following a process that has just started.</summary>
    /// <param name="process">The process.</param>
    /// <returns>A monitor, or null on a platform without one.</returns>
    public static IResourceMonitor? Start(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600) ? new WindowsResourceMonitor(process)
            : OperatingSystem.IsLinux() ? new LinuxResourceMonitor(process.Id, NativeMethods.ClockTicks())
            : OperatingSystem.IsMacOS() ? new MacResourceMonitor(process.Id)
            : null;
    }
}
