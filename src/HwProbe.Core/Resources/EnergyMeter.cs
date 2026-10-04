namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads the whole-device energy counters readable without root: NVIDIA's through NVML, the CPU package's through Windows' Energy Meter counters or Linux's powercap, and Intel discrete GPUs' through hwmon.</summary>
/// <remarks>Linux powercap is root-only on most kernels (CVE-2020-8694) and masked in containers, and Intel's integrated GPUs have no unprivileged meter, so on many servers there are none.</remarks>
internal static class EnergyMeter
{
    // Long enough to settle a GPU's clocks from the previous run, short enough not to stretch a test much.
    private static readonly TimeSpan _idle = TimeSpan.FromSeconds(1);

    private static readonly Lazy<List<IEnergySource>> _sources = new(Discover);

    /// <summary>Gets a value indicating whether any meter is readable.</summary>
    public static bool Available => _sources.Value.Count > 0;

    /// <summary>Reads every meter.</summary>
    /// <returns>Joules by source, leaving out failed reads.</returns>
    public static Dictionary<IEnergySource, double> Read()
    {
        Dictionary<IEnergySource, double> readings = [];
        foreach (var source in _sources.Value)
        {
            try
            {
                if (source.Read() is { } joules)
                {
                    readings[source] = joules;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                // A meter that stops being readable is left out of this reading.
            }
        }

        return readings;
    }

    /// <summary>Returns the joules each domain used between two readings.</summary>
    /// <param name="start">The first reading.</param>
    /// <param name="end">The second.</param>
    /// <returns>Joules by domain, or null when no source was read both times.</returns>
    public static Dictionary<string, double>? Used(Dictionary<IEnergySource, double> start, Dictionary<IEnergySource, double> end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        var used = end.Where(e => start.ContainsKey(e.Key))
            .GroupBy(e => e.Key.Domain, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(e => Delta(e.Key, start[e.Key], e.Value)), StringComparer.Ordinal);
        return used.Count > 0 ? used : null;
    }

    /// <summary>Measures each domain's power with nothing of HwProbe's running.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Watts by domain, or null without meters.</returns>
    public static async Task<Dictionary<string, double>?> IdleWattsAsync(CancellationToken cancellationToken)
    {
        if (!Available)
        {
            return null;
        }

        var start = Read();
        await Task.Delay(_idle, cancellationToken);
        return Used(start, Read())?.ToDictionary(d => d.Key, d => d.Value / _idle.TotalSeconds, StringComparer.Ordinal);
    }

    /// <summary>Returns the energy between two reads of one counter, allowing for one wrap.</summary>
    /// <param name="source">The counter.</param>
    /// <param name="start">The first read.</param>
    /// <param name="end">The second.</param>
    /// <returns>The joules.</returns>
    private static double Delta(IEnergySource source, double start, double end) =>
        end >= start ? end - start : source.WrapJoules is { } wrap ? end + wrap - start : 0;

    /// <summary>Finds the meters this host lets HwProbe read.</summary>
    /// <returns>The sources.</returns>
    private static List<IEnergySource> Discover()
    {
        List<IEnergySource> sources = [];
        try
        {
            if (Nvml.Available && Nvml.TotalEnergyJoules() is not null)
            {
                sources.Add(new EnergySource("Gpu", null, Nvml.TotalEnergyJoules));
            }

            if (OperatingSystem.IsWindows())
            {
                sources.AddRange(WindowsEnergy.Sources());
            }
            else if (OperatingSystem.IsLinux())
            {
                sources.AddRange(LinuxEnergy.Sources());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Whatever was found before the failure is used.
        }

        return sources;
    }
}
