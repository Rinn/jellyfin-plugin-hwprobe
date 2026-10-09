using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Devices;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads the CPU time the server has spent busy on Linux: its own cgroup's, else the whole host's from <c>/proc/stat</c>.</summary>
/// <remarks>
/// A container sees the host's <c>/proc/stat</c>, which counts other containers' load too; its cgroup counts only its own
/// processes (cgroup v2 <c>cpu.stat</c> usage_usec, v1 <c>cpuacct.usage</c> in nanoseconds).
/// </remarks>
/// <param name="platform">Host access.</param>
public sealed class ServerCpu(IHostPlatform platform)
{
    private const string CgroupRoot = "/sys/fs/cgroup";

    // USER_HZ, which proc(5) gives as 100 on most architectures; only the fallback uses it.
    private const double TicksPerSecond = 100;

    /// <summary>Reads the busy CPU time so far, across all cores.</summary>
    /// <returns>Seconds, or null off Linux or when nothing can be read.</returns>
    public double? BusySeconds()
    {
        if (platform.Os != HostOs.Linux)
        {
            return null;
        }

        return Cgroup() ?? Host();
    }

    /// <summary>Reads the server's cgroup CPU time, cgroup v2 first, then v1.</summary>
    /// <returns>Seconds, or null when it can't be read.</returns>
    private double? Cgroup()
    {
        var membership = platform.TryReadText("/proc/self/cgroup") ?? string.Empty;
        foreach (var line in membership.Split('\n'))
        {
            var parts = line.Split(':', 3);
            if (parts.Length != 3)
            {
                continue;
            }

            // A container usually sees its own cgroup at the root; outside one, the path names it.
            var path = parts[2].TrimEnd('/');
            if (parts[0] == "0" && parts[1].Length == 0 && V2(CgroupRoot + path) is { } v2)
            {
                return v2;
            }

            if (parts[1].Split(',').Contains("cpuacct") && (V1(CgroupRoot + "/cpuacct" + path) ?? V1(CgroupRoot + "/cpu,cpuacct" + path)) is { } v1)
            {
                return v1;
            }
        }

        return V2(CgroupRoot) ?? V1(CgroupRoot + "/cpuacct") ?? V1(CgroupRoot + "/cpu,cpuacct");
    }

    /// <summary>Reads a cgroup v2 directory's CPU time.</summary>
    /// <param name="directory">The cgroup's directory.</param>
    /// <returns>Seconds, or null.</returns>
    private double? V2(string directory) =>
        platform.TryReadText($"{directory}/cpu.stat") is { } stat && MemoryHeadroom.Field(stat, "usage_usec") is { } micros ? micros / 1e6 : null;

    /// <summary>Reads a cgroup v1 cpuacct directory's CPU time.</summary>
    /// <param name="directory">The cgroup's directory.</param>
    /// <returns>Seconds, or null.</returns>
    private double? V1(string directory) =>
        long.TryParse(platform.TryReadText($"{directory}/cpuacct.usage")?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var nanos) ? nanos / 1e9 : null;

    /// <summary>Reads the host's busy CPU time from <c>/proc/stat</c>: everything in the cpu line but idle and iowait.</summary>
    /// <returns>Seconds, or null.</returns>
    private double? Host()
    {
        var line = platform.TryReadText("/proc/stat")?.Split('\n').FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        var fields = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(f => long.TryParse(f, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (long?)null).ToList();
        if (fields is not { Count: >= 5 } || fields.Any(f => f is null))
        {
            return null;
        }

        return (fields.Sum(f => f ?? 0) - (fields[3] ?? 0) - (fields[4] ?? 0)) / TicksPerSecond;
    }
}
