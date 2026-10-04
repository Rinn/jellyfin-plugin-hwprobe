using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads Windows's "GPU Engine" performance counter instance names, e.g. <c>pid_1234_luid_0x0_0x0000C2F3_phys_0_eng_3_engtype_VideoDecode</c>.</summary>
public static class GpuEngineCounters
{
    /// <summary>Returns the process and engine type an instance belongs to.</summary>
    /// <param name="instance">The instance name.</param>
    /// <returns>The process id and engine type, or null for a name that doesn't parse.</returns>
    public static (int Pid, string Engine)? Parse(string instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        const string Pid = "pid_";
        const string Type = "_engtype_";
        var end = instance.IndexOf('_', Pid.Length);
        var type = instance.LastIndexOf(Type, StringComparison.Ordinal);
        return instance.StartsWith(Pid, StringComparison.Ordinal) && end > Pid.Length && type > end
            && int.TryParse(instance.AsSpan(Pid.Length, end - Pid.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            ? (pid, instance[(type + Type.Length)..])
            : null;
    }
}
