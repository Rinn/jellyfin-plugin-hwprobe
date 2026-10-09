using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Devices;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads how much memory the server has to spare on Linux, from <c>/proc/meminfo</c> and its own cgroup.</summary>
/// <remarks>
/// An Intel GPU's video surfaces are shared memory charged to the cgroup but to no process, so they show here and in no
/// ffmpeg's RSS. The cgroup's use excludes its inactive page cache, which it reclaims first, as cAdvisor's working set does.
/// </remarks>
/// <param name="platform">Host access.</param>
public sealed class MemoryHeadroom(IHostPlatform platform)
{
    private const string CgroupRoot = "/sys/fs/cgroup";

    /// <summary>Reads the memory now.</summary>
    /// <returns>The snapshot, or null off Linux or when <c>/proc/meminfo</c> can't be read.</returns>
    public MemorySnapshot? Read()
    {
        if (platform.Os != HostOs.Linux || platform.TryReadText("/proc/meminfo") is not { } meminfo
            || Field(meminfo, "MemAvailable:") is not { } availableKb || Field(meminfo, "MemTotal:") is not { } totalKb)
        {
            return null;
        }

        var available = availableKb * 1024;
        var total = totalKb * 1024;
        if (Cgroup() is ({ } limit, { } used) && limit < total)
        {
            available = Math.Min(available, Math.Max(0, limit - used));
            total = limit;
        }

        return new MemorySnapshot(available, total);
    }

    /// <summary>Reads a number from a <c>key value</c> line, such as <c>MemAvailable: 123 kB</c> or <c>inactive_file 123</c>.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="key">The line's first word.</param>
    /// <returns>The number, or null when the line is missing.</returns>
    internal static long? Field(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == key && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Reads the server's cgroup memory limit and its use less inactive page cache, cgroup v2 first, then v1.</summary>
    /// <returns>The limit and use in bytes, or nulls when there's no limit or it can't be read.</returns>
    private (long? Limit, long? Used) Cgroup()
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
            if (parts[0] == "0" && parts[1].Length == 0
                && Read(CgroupRoot + parts[2].TrimEnd('/'), "memory.max", "memory.current", "inactive_file") is ({ } limit2, { } used2))
            {
                return (limit2, used2);
            }

            if (parts[1].Split(',').Contains("memory")
                && Read(CgroupRoot + "/memory" + parts[2].TrimEnd('/'), "memory.limit_in_bytes", "memory.usage_in_bytes", "total_inactive_file") is ({ } limit1, { } used1))
            {
                return (limit1, used1);
            }
        }

        return Read(CgroupRoot, "memory.max", "memory.current", "inactive_file") is ({ } limit, { } used) ? (limit, used)
            : Read(CgroupRoot + "/memory", "memory.limit_in_bytes", "memory.usage_in_bytes", "total_inactive_file");
    }

    /// <summary>Reads one cgroup directory's limit and use.</summary>
    /// <param name="directory">The cgroup's directory.</param>
    /// <param name="limitFile">The limit file.</param>
    /// <param name="usageFile">The usage file.</param>
    /// <param name="inactiveKey">The memory.stat key for inactive page cache.</param>
    /// <returns>The limit and use, or nulls when either can't be read or there's no limit (<c>max</c>).</returns>
    private (long? Limit, long? Used) Read(string directory, string limitFile, string usageFile, string inactiveKey)
    {
        var limitText = platform.TryReadText($"{directory}/{limitFile}")?.Trim();
        var usageText = platform.TryReadText($"{directory}/{usageFile}")?.Trim();
        if (!long.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            || !long.TryParse(usageText, NumberStyles.None, CultureInfo.InvariantCulture, out var usage))
        {
            return (null, null);
        }

        var inactive = platform.TryReadText($"{directory}/memory.stat") is { } stat ? Field(stat, inactiveKey) ?? 0 : 0;
        return (limit, Math.Max(0, usage - inactive));
    }
}
