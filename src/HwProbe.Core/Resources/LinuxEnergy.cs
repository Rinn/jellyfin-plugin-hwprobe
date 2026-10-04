using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Energy counters in Linux's sysfs: the CPU packages' powercap (RAPL) zones and Intel discrete GPUs' hwmon, each used only when readable.</summary>
internal static class LinuxEnergy
{
    private const double JoulesPerMicrojoule = 1e-6;

    /// <summary>Returns the readable meters.</summary>
    /// <returns>The sources.</returns>
    public static IEnumerable<IEnergySource> Sources()
    {
        // Top-level zones are packages (intel-rapl:0, intel-rapl:1); their subzones are parts of them. AMD CPUs use the same driver.
        const string Powercap = "/sys/class/powercap";
        if (Directory.Exists(Powercap))
        {
            foreach (var zone in Directory.EnumerateDirectories(Powercap, "intel-rapl:*").Where(z => Path.GetFileName(z).Count(c => c == ':') == 1))
            {
                var energy = Path.Combine(zone, "energy_uj");
                if (Name(zone)?.StartsWith("package", StringComparison.Ordinal) == true && Microjoules(energy) is not null)
                {
                    var wrap = Microjoules(Path.Combine(zone, "max_energy_range_uj"));
                    yield return new EnergySource("Cpu", wrap * JoulesPerMicrojoule, () => Microjoules(energy) * JoulesPerMicrojoule);
                }
            }
        }

        // i915 and xe report energy for discrete GPUs only (Arc), from kernel 6.2.
        const string Drm = "/sys/class/drm";
        if (Directory.Exists(Drm))
        {
            foreach (var card in Directory.EnumerateDirectories(Drm, "card*").Where(c => !Path.GetFileName(c).Contains('-', StringComparison.Ordinal)))
            {
                var hwmon = Path.Combine(card, "device", "hwmon");
                if (!Directory.Exists(hwmon))
                {
                    continue;
                }

                foreach (var energy in Directory.EnumerateDirectories(hwmon).Select(h => Path.Combine(h, "energy1_input")).Where(e => Microjoules(e) is not null))
                {
                    yield return new EnergySource("Gpu", null, () => Microjoules(energy) * JoulesPerMicrojoule);
                }
            }
        }
    }

    /// <summary>Reads a powercap zone's name.</summary>
    /// <param name="zone">The zone's folder.</param>
    /// <returns>The name, or null when unreadable, so one zone doesn't hide the others.</returns>
    private static string? Name(string zone)
    {
        try
        {
            return File.ReadAllText(Path.Combine(zone, "name")).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads a counter file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its value, or null when it's missing or unreadable (powercap is root-only on most kernels).</returns>
    private static double? Microjoules(string path)
    {
        try
        {
            return double.Parse(File.ReadAllText(path).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }
}
