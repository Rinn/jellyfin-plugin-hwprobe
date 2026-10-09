using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Devices;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads the CPU time the whole host has spent busy, from <c>/proc/stat</c> on Linux.</summary>
/// <remarks>Its counters are in USER_HZ, 100 a second on every Linux architecture (proc(5)).</remarks>
/// <param name="platform">Host access.</param>
public sealed class HostCpu(IHostPlatform platform)
{
    private const double TicksPerSecond = 100;

    /// <summary>Reads the busy CPU time so far, across all cores.</summary>
    /// <returns>Seconds, or null off Linux or when <c>/proc/stat</c> can't be read.</returns>
    public double? BusySeconds()
    {
        if (platform.Os != HostOs.Linux || platform.TryReadText("/proc/stat") is not { } stat)
        {
            return null;
        }

        // cpu user nice system idle iowait irq softirq steal ...: busy is everything but idle and iowait.
        var line = stat.Split('\n').FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        var fields = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(f => long.TryParse(f, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (long?)null).ToList();
        if (fields is not { Count: >= 5 } || fields.Any(f => f is null))
        {
            return null;
        }

        var total = fields.Sum(f => f ?? 0);
        return (total - (fields[3] ?? 0) - (fields[4] ?? 0)) / TicksPerSecond;
    }
}
