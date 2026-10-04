using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>The PDH calls that read the "GPU Engine" and "GPU Process Memory" performance counters.</summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsNativeMethods
{
    // Every engine and every process's memory; the monitor keeps its own process's.
    private const string RunningTime = @"\GPU Engine(*)\Running Time";
    private const string DedicatedUsage = @"\GPU Process Memory(*)\Dedicated Usage";
    private const uint MoreData = 0x800007D2;

    /// <summary>Opens a query on every GPU engine's running time and every process's dedicated GPU memory.</summary>
    /// <returns>The query and counter handles (the memory counter is 0 when unavailable), or null when the engine counters aren't available.</returns>
    public static (nint Query, nint Engines, nint Memory)? OpenGpuQuery()
    {
        if (PdhOpenQuery(null, 0, out var query) != 0)
        {
            return null;
        }

        if (PdhAddEnglishCounter(query, RunningTime, 0, out var engines) != 0)
        {
            _ = PdhCloseQuery(query);
            return null;
        }

        return (query, engines, PdhAddEnglishCounter(query, DedicatedUsage, 0, out var memory) == 0 ? memory : 0);
    }

    /// <summary>Collects every counter in a query once.</summary>
    /// <param name="query">The query.</param>
    /// <returns>True when the values were collected.</returns>
    public static bool Collect(nint query) => PdhCollectQueryData(query) == 0;

    /// <summary>Returns a counter's raw value for each instance, from the last <see cref="Collect"/>.</summary>
    /// <param name="counter">The counter.</param>
    /// <returns>Raw values by instance name: 100-nanosecond units for running time, bytes for memory; empty when unavailable.</returns>
    public static Dictionary<string, long> ReadRaw(nint counter)
    {
        var times = new Dictionary<string, long>(StringComparer.Ordinal);
        if (counter == 0)
        {
            return times;
        }

        uint size = 0;
        if (PdhGetRawCounterArray(counter, ref size, out _, 0) != MoreData || size == 0)
        {
            return times;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetRawCounterArray(counter, ref size, out var count, buffer) != 0)
            {
                return times;
            }

            // PDH_RAW_COUNTER_ITEM_W: a name pointer, then PDH_RAW_COUNTER (CStatus, TimeStamp, FirstValue, SecondValue, MultiCount).
            var itemSize = Marshal.SizeOf<RawCounterItem>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<RawCounterItem>(buffer + (i * itemSize));
                if (Marshal.PtrToStringUni(item.Name) is { } name)
                {
                    times[name] = item.FirstValue;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return times;
    }

    /// <summary>Closes a query.</summary>
    /// <param name="query">The query.</param>
    public static void CloseQuery(nint query) => _ = PdhCloseQuery(query);

    /// <summary>PdhOpenQueryW.</summary>
    /// <param name="dataSource">Null for live data.</param>
    /// <param name="userData">Unused.</param>
    /// <param name="query">Receives the query.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    /// <summary>PdhAddEnglishCounterW.</summary>
    /// <param name="query">The query.</param>
    /// <param name="path">The counter path, in English whatever the system language.</param>
    /// <param name="userData">Unused.</param>
    /// <param name="counter">Receives the counter.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhAddEnglishCounter(nint query, string path, nint userData, out nint counter);

    /// <summary>PdhCollectQueryData.</summary>
    /// <param name="query">The query.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport("pdh.dll", EntryPoint = "PdhCollectQueryData")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCollectQueryData(nint query);

    /// <summary>PdhGetRawCounterArrayW.</summary>
    /// <param name="counter">The counter.</param>
    /// <param name="bufferSize">The buffer's size in bytes; receives the size needed.</param>
    /// <param name="itemCount">Receives the number of items.</param>
    /// <param name="buffer">The buffer, or 0 to ask for its size.</param>
    /// <returns>0 on success, or PDH_MORE_DATA when the buffer is too small.</returns>
    [LibraryImport("pdh.dll", EntryPoint = "PdhGetRawCounterArrayW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhGetRawCounterArray(nint counter, ref uint bufferSize, out uint itemCount, nint buffer);

    /// <summary>PdhCloseQuery.</summary>
    /// <param name="query">The query.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport("pdh.dll", EntryPoint = "PdhCloseQuery")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCloseQuery(nint query);

    /// <summary>PDH_RAW_COUNTER_ITEM_W.</summary>
    /// <param name="Name">The instance name.</param>
    /// <param name="Status">The counter's status.</param>
    /// <param name="TimeStamp">When it was collected, as a FILETIME.</param>
    /// <param name="FirstValue">The raw value.</param>
    /// <param name="SecondValue">The second raw value, for counters that have one.</param>
    /// <param name="MultiCount">The count for multi-instance counters.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RawCounterItem(nint Name, uint Status, long TimeStamp, long FirstValue, long SecondValue, uint MultiCount);
}
