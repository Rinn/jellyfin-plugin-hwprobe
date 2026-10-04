using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Turns a catalog test suite into the runs it makes on a given server.</summary>
public static class SpeedSuites
{
    private const string ThreadOption = "EncodingThreadCount";

    /// <summary>Returns a suite's runs, in order.</summary>
    /// <param name="suite">The suite.</param>
    /// <param name="processorCount">The server's logical CPU count, which bounds the thread limits.</param>
    /// <returns>The runs.</returns>
    public static IReadOnlyList<SuiteStep> Steps(CatalogSuite suite, int processorCount)
    {
        ArgumentNullException.ThrowIfNull(suite);
        if (!suite.ThreadSteps)
        {
            return [.. suite.Steps.Select(s => new SuiteStep(s.Label, s.Options, s.Videos ?? suite.Videos, s.Outputs ?? suite.Outputs))];
        }

        // Auto, then doubling thread limits, then the CPU count itself; only limits the setting offers.
        var option = Catalog.Default.Options.First(o => o.Key == ThreadOption);
        List<int> limits = [];
        for (var n = 1; n < processorCount; n *= 2)
        {
            limits.Add(n);
        }

        limits.Add(processorCount);
        var values = limits.Select(n => n.ToString(CultureInfo.InvariantCulture)).Where(option.Takes).Distinct(StringComparer.Ordinal);
        return [.. new[] { ("-1", "Auto") }.Concat(values.Select(v => (v, v + (v == "1" ? " thread" : " threads"))))
            .Select(v => new SuiteStep(v.Item2, new Dictionary<string, string>(StringComparer.Ordinal) { [ThreadOption] = v.Item1 }, suite.Videos, suite.Outputs))];
    }

    /// <summary>Returns the backends a suite runs on.</summary>
    /// <param name="suite">The suite.</param>
    /// <param name="configured">The server's configured backend.</param>
    /// <returns>The backends, the configured one first; software is <see cref="HwType.none"/>.</returns>
    public static IReadOnlyList<HwType> Backends(CatalogSuite suite, HwType configured)
    {
        ArgumentNullException.ThrowIfNull(suite);
        return suite.Backends switch
        {
            "software" => [HwType.none],
            "configured" => configured == HwType.none ? [] : [configured],
            _ => configured == HwType.none ? [HwType.none] : [configured, HwType.none],
        };
    }

    /// <summary>Returns whether a suite can run on this server.</summary>
    /// <param name="suite">The suite.</param>
    /// <param name="report">The latest probe.</param>
    /// <param name="configured">The server's configured backend.</param>
    /// <returns>False when it needs something the server lacks, such as a working low-power encoder on QSV.</returns>
    public static bool Offered(CatalogSuite suite, CapabilityReport report, HwType configured)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(report);
        var backends = Backends(suite, configured);
        var working = backends.All(b => b == HwType.none || report.Backends.Any(r => r.Type == b && r.Verdict == BackendVerdict.Viable));
        return backends.Count > 0 && working && suite.Requires switch
        {
            "lowPower" => configured == HwType.qsv && report.Backends.Any(b => b.Type == HwType.qsv && b.Encode.Any(e => e.Key.EndsWith("_lowpower", StringComparison.Ordinal) && e.Value == ProbeOutcome.Pass)),
            _ => true,
        };
    }
}
