using System.Runtime.Versioning;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>The CPU package's energy from Windows' Energy Meter performance counters (the Energy Metering Interface over RAPL), readable without admin rights.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsEnergy
{
    // Raw values are picowatt-hours: on an RTX 5080 PC their change matched the Power counter times the time.
    private const double JoulesPerPicowattHour = 3.6e-9;

    /// <summary>Returns the package meter, when the counters exist.</summary>
    /// <returns>Zero or one source.</returns>
    public static IEnumerable<IEnergySource> Sources()
    {
        if (WindowsNativeMethods.OpenCounter(@"\Energy Meter(*)\Energy") is not { } meter)
        {
            yield break;
        }

        // The query stays open for the process's life, as the meter is read on every measurement.
        var gate = new Lock();
        double? Read()
        {
            lock (gate)
            {
                if (!WindowsNativeMethods.Collect(meter.Query))
                {
                    return null;
                }

                var packages = WindowsNativeMethods.ReadRaw(meter.Counter).Where(i => i.Key.EndsWith("_PKG", StringComparison.Ordinal)).ToList();
                return packages.Count > 0 ? packages.Sum(i => i.Value) * JoulesPerPicowattHour : null;
            }
        }

        if (Read() is not null)
        {
            yield return new EnergySource("Cpu", null, Read);
        }
    }
}
