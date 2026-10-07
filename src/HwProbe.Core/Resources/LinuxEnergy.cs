using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Energy meters in Linux's sysfs: the CPU packages' powercap (RAPL) zones, Intel discrete GPUs' hwmon energy, and AMD GPUs' hwmon power, each used only when readable.</summary>
internal static class LinuxEnergy
{
    private const double JoulesPerMicrojoule = 1e-6;
    private const double WattsPerMicrowatt = 1e-6;
    private const string Drm = "/sys/class/drm";

    // Instantaneous first: sampled every 200 ms, it follows a run more closely than the firmware's average.
    private static readonly string[] _amdPowerFiles = ["power1_input", "power1_average"];

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
                if (Name(zone)?.StartsWith("package", StringComparison.Ordinal) == true && ReadNumber(energy) is not null)
                {
                    var wrap = ReadNumber(Path.Combine(zone, "max_energy_range_uj"));
                    yield return new EnergySource("Cpu", wrap * JoulesPerMicrojoule, () => ReadNumber(energy) * JoulesPerMicrojoule);
                }
            }
        }

        // i915 and xe report energy for discrete GPUs only (Arc), from kernel 6.2.
        foreach (var energy in GpuHwmons().Select(h => Path.Combine(h, "energy1_input")).Where(e => ReadNumber(e) is not null))
        {
            yield return new EnergySource("Gpu", null, () => ReadNumber(energy) * JoulesPerMicrojoule);
        }
    }

    /// <summary>Returns the readable power meters: AMD GPUs', which report power and no energy.</summary>
    /// <returns>The sources.</returns>
    /// <remarks>
    /// amdgpu's power1_input is instantaneous and power1_average averaged by the firmware, both in microwatts; not every GPU has
    /// both, and on APUs they include the CPU (drivers/gpu/drm/amd/pm/amdgpu_pm.c, hwmon interfaces for GPU power).
    /// </remarks>
    public static IEnumerable<IPowerSource> PowerSources()
    {
        foreach (var hwmon in GpuHwmons().Where(h => Name(h) == "amdgpu"))
        {
            if (_amdPowerFiles.Select(f => Path.Combine(hwmon, f)).FirstOrDefault(f => ReadNumber(f) is not null) is { } power)
            {
                yield return new PowerSource("Gpu", () => ReadNumber(power) * WattsPerMicrowatt);
            }
        }
    }

    /// <summary>Returns the hwmon folders of every DRM card.</summary>
    /// <returns>The folders.</returns>
    private static IEnumerable<string> GpuHwmons()
    {
        if (!Directory.Exists(Drm))
        {
            yield break;
        }

        // Connectors are named card0-HDMI-A-1 and so on.
        foreach (var card in Directory.EnumerateDirectories(Drm, "card*").Where(c => !Path.GetFileName(c).Contains('-', StringComparison.Ordinal)))
        {
            var hwmon = Path.Combine(card, "device", "hwmon");
            if (!Directory.Exists(hwmon))
            {
                continue;
            }

            foreach (var folder in Directory.EnumerateDirectories(hwmon))
            {
                yield return folder;
            }
        }
    }

    /// <summary>Reads a powercap zone's or hwmon device's name.</summary>
    /// <param name="folder">The zone's or device's folder.</param>
    /// <returns>The name, or null when unreadable, so one doesn't hide the others.</returns>
    private static string? Name(string folder)
    {
        try
        {
            return File.ReadAllText(Path.Combine(folder, "name")).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads a counter or meter file: microjoules or microwatts.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its value, or null when it's missing or unreadable (powercap is root-only on most kernels).</returns>
    private static double? ReadNumber(string path)
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
