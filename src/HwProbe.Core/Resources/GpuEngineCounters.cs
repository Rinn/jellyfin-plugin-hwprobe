using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads Windows's "GPU Engine" and "GPU Process Memory" performance counter instance names, e.g. <c>pid_1234_luid_0x0_0x0000C2F3_phys_0_eng_3_engtype_VideoDecode</c>.</summary>
public static class GpuEngineCounters
{
    /// <summary>Returns the process and engine type an instance belongs to.</summary>
    /// <param name="instance">The instance name.</param>
    /// <returns>The process id and engine type, or null for a name that doesn't parse.</returns>
    public static (int Pid, string Engine)? Parse(string instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        const string Type = "_engtype_";
        var type = instance.LastIndexOf(Type, StringComparison.Ordinal);
        return Pid(instance) is { } pid && type > 0 ? (pid, instance[(type + Type.Length)..]) : null;
    }

    /// <summary>Returns the process an instance belongs to.</summary>
    /// <param name="instance">The instance name, e.g. <c>pid_1234_luid_0x0_0x0000C2F3_phys_0</c>.</param>
    /// <returns>The process id, or null for a name that doesn't parse.</returns>
    public static int? Pid(string instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        const string Prefix = "pid_";
        var end = instance.Length > Prefix.Length ? instance.IndexOf('_', Prefix.Length) : -1;
        return instance.StartsWith(Prefix, StringComparison.Ordinal) && end > Prefix.Length
            && int.TryParse(instance.AsSpan(Prefix.Length, end - Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            ? pid
            : null;
    }
}
