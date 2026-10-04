using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads Linux's per-process files under <c>/proc</c>; text in, numbers out, so it's tested without Linux.</summary>
public static class ProcFiles
{
    /// <summary>Returns the user and system CPU time from <c>/proc/[pid]/stat</c>, in clock ticks.</summary>
    /// <param name="stat">The file's text.</param>
    /// <returns>The ticks, or null when the text doesn't parse.</returns>
    public static long? CpuTicks(string stat)
    {
        ArgumentNullException.ThrowIfNull(stat);

        // The command name is in parentheses and may hold spaces or parentheses itself; fields follow the last ')'.
        var end = stat.LastIndexOf(')');
        if (end < 0)
        {
            return null;
        }

        // proc(5): after the name come state (field 3) ... utime (14) and stime (15).
        var fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 12
            && long.TryParse(fields[11], NumberStyles.None, CultureInfo.InvariantCulture, out var user)
            && long.TryParse(fields[12], NumberStyles.None, CultureInfo.InvariantCulture, out var system)
            ? user + system
            : null;
    }

    /// <summary>Returns the peak resident memory (<c>VmHWM</c>) from <c>/proc/[pid]/status</c>.</summary>
    /// <param name="status">The file's text.</param>
    /// <returns>The bytes, or null when the line is missing.</returns>
    public static long? PeakBytes(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        foreach (var line in status.Split('\n'))
        {
            if (line.StartsWith("VmHWM:", StringComparison.Ordinal))
            {
                var value = line["VmHWM:".Length..].Trim().Split(' ')[0];
                return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var kb) ? kb * 1024 : null;
            }
        }

        return null;
    }

    /// <summary>Returns a DRM client's engine counters from <c>/proc/[pid]/fdinfo/[fd]</c> (kernel drm-usage-stats).</summary>
    /// <param name="fdinfo">The file's text.</param>
    /// <returns>The client, keyed by device and client id, with busy nanoseconds by engine (i915, amdgpu) and busy and total cycles by engine (xe); null when the descriptor isn't a DRM client.</returns>
    public static DrmClient? Drm(string fdinfo)
    {
        ArgumentNullException.ThrowIfNull(fdinfo);
        string? client = null;
        var device = string.Empty;
        var nanoseconds = new Dictionary<string, long>(StringComparer.Ordinal);
        var cycles = new Dictionary<string, (long Busy, long Total)>(StringComparer.Ordinal);
        long? resident = null;
        long? legacy = null;
        foreach (var raw in fdinfo.Split('\n'))
        {
            var colon = raw.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var key = raw[..colon];
            var parts = raw[(colon + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var value = parts.Length > 0 ? parts[0] : string.Empty;
            if (key == "drm-client-id")
            {
                client = value;
            }
            else if (key == "drm-pdev")
            {
                // Client ids are only unique per device on some kernels.
                device = value;
            }
            else if (key.StartsWith("drm-engine-", StringComparison.Ordinal) && !key.StartsWith("drm-engine-capacity-", StringComparison.Ordinal) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ns))
            {
                nanoseconds[key["drm-engine-".Length..]] = ns;
            }
            else if (key.StartsWith("drm-cycles-", StringComparison.Ordinal) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var busy))
            {
                var engine = key["drm-cycles-".Length..];
                cycles[engine] = (busy, cycles.GetValueOrDefault(engine).Total);
            }
            else if (key.StartsWith("drm-total-cycles-", StringComparison.Ordinal) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var total))
            {
                var engine = key["drm-total-cycles-".Length..];
                cycles[engine] = (cycles.GetValueOrDefault(engine).Busy, total);
            }
            else if (key.StartsWith("drm-resident-", StringComparison.Ordinal) && Bytes(parts) is { } bytes)
            {
                // drm-usage-stats memory keys (kernel 6.4+), one per region such as system0 or vram0.
                resident = (resident ?? 0) + bytes;
            }
            else if (key.StartsWith("drm-memory-", StringComparison.Ordinal) && Bytes(parts) is { } older)
            {
                // amdgpu's keys before the common ones (vram, gtt, cpu).
                legacy = (legacy ?? 0) + older;
            }
        }

        return client is null ? null : new DrmClient(device.Length > 0 ? device + "/" + client : client, nanoseconds, cycles) { ResidentBytes = resident ?? legacy };
    }

    /// <summary>Reads a drm-usage-stats memory value, a number with an optional KiB, MiB, or GiB unit.</summary>
    /// <param name="parts">The value and its unit.</param>
    /// <returns>The bytes, or null when the number doesn't parse.</returns>
    private static long? Bytes(string[] parts)
    {
        if (parts.Length == 0 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        return (parts.Length > 1 ? parts[1] : string.Empty) switch
        {
            "KiB" => number * 1024,
            "MiB" => number * 1024 * 1024,
            "GiB" => number * 1024 * 1024 * 1024,
            _ => number,
        };
    }
}
