using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>The few libc and libproc calls the Linux and macOS monitors need.</summary>
internal static partial class NativeMethods
{
    // sysconf's name for clock ticks a second on Linux (bits/confname.h).
    private const int ScClockTicks = 2;

    // rusage_info_v4 (sys/resource.h): a 16-byte uuid, then 64-bit fields; the indexes count those fields.
    private const int RusageInfoV4 = 4;
    private const int UserTimeField = 0;
    private const int SystemTimeField = 1;
    private const int LifetimeMaxFootprintField = 28;
    private const int V4Fields = 35;

    private static readonly Lazy<double> _machSecondsPerUnit = new(() =>
    {
        // CPU times in rusage are mach absolute-time units, which aren't nanoseconds on Apple silicon.
        _ = MachTimebaseInfo(out var info);
        return info.Denominator == 0 ? 1e-9 : (double)info.Numerator / info.Denominator / 1e9;
    });

    /// <summary>Returns the kernel's clock ticks a second.</summary>
    /// <returns>The ticks, or 100 when sysconf fails.</returns>
    public static long ClockTicks() => Sysconf(ScClockTicks) is var ticks and > 0 ? ticks : 100;

    /// <summary>Reads a process's CPU time and lifetime peak memory.</summary>
    /// <param name="pid">The process.</param>
    /// <returns>The CPU time in mach units and the peak footprint in bytes, or null when the process can't be read.</returns>
    public static (ulong CpuTime, ulong LifetimeMaxFootprint)? ProcPidRusage(int pid)
    {
        var buffer = new byte[16 + (8 * V4Fields)];
        if (ProcPidRusageNative(pid, RusageInfoV4, buffer) != 0)
        {
            return null;
        }

        ulong Field(int index) => BitConverter.ToUInt64(buffer, 16 + (8 * index));
        return (Field(UserTimeField) + Field(SystemTimeField), Field(LifetimeMaxFootprintField));
    }

    /// <summary>Converts mach absolute-time units to seconds.</summary>
    /// <param name="units">The units.</param>
    /// <returns>The seconds.</returns>
    public static double MachSeconds(ulong units) => units * _machSecondsPerUnit.Value;

    /// <summary>libc's sysconf.</summary>
    /// <param name="name">The setting.</param>
    /// <returns>Its value, or -1.</returns>
    [LibraryImport("libc", EntryPoint = "sysconf")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial long Sysconf(int name);

    /// <summary>libproc's proc_pid_rusage.</summary>
    /// <param name="pid">The process.</param>
    /// <param name="flavor">The rusage_info version.</param>
    /// <param name="buffer">Receives the structure.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "proc_pid_rusage")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int ProcPidRusageNative(int pid, int flavor, [Out] byte[] buffer);

    /// <summary>mach_timebase_info.</summary>
    /// <param name="info">Receives the ratio of absolute-time units to nanoseconds.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "mach_timebase_info")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int MachTimebaseInfo(out MachTimebase info);

    /// <summary>mach_timebase_info_data_t.</summary>
    /// <param name="Numerator">Nanoseconds per <see cref="Denominator"/> units.</param>
    /// <param name="Denominator">Units per <see cref="Numerator"/> nanoseconds.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MachTimebase(uint Numerator, uint Denominator);
}
