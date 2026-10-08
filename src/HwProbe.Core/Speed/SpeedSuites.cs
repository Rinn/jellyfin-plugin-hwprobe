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
    /// <param name="server">The server's settings; when a suite varies one setting and none of its steps is the server's value, that value is added as a last step, since suggestions compare against it. Null leaves the steps as they are.</param>
    /// <param name="hardware">The hardware backend the suite runs on, which leaves out steps for other backends; null keeps every step.</param>
    /// <returns>The runs, each with the suite's shared settings under its own.</returns>
    public static IReadOnlyList<SuiteStep> Steps(CatalogSuite suite, int processorCount, SpeedSettings? server = null, HwType? hardware = null)
    {
        ArgumentNullException.ThrowIfNull(suite);
        return [.. VariedSteps(suite, processorCount, server, hardware).Select(s => s with { Options = suite.Options.Where(o => !s.Options.ContainsKey(o.Key)).Concat(s.Options).ToDictionary(StringComparer.Ordinal) })];
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
            "configured" => [configured],
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

    /// <summary>Returns a suite's runs with only the settings each varies, plus the server's value of a varied setting none of them is.</summary>
    /// <param name="suite">The suite.</param>
    /// <param name="processorCount">The server's logical CPU count.</param>
    /// <param name="server">The server's settings, or null.</param>
    /// <param name="hardware">The hardware backend, or null to keep every step.</param>
    /// <returns>The runs.</returns>
    private static IReadOnlyList<SuiteStep> VariedSteps(CatalogSuite suite, int processorCount, SpeedSettings? server, HwType? hardware)
    {
        var steps = CatalogSteps(suite, processorCount, hardware);
        var keys = steps.SelectMany(s => s.Options.Keys).Distinct(StringComparer.Ordinal).ToList();
        if (server is null || keys is not [var key] || steps.Any(s => !s.Options.ContainsKey(key)) || !SpeedAdvisor.Settings.Contains(key))
        {
            return steps;
        }

        var value = SpeedAdvisor.ValueOf(server, key);
        if (steps.Any(s => s.Options[key] == value) || Catalog.Default.Option(key) is not { } option || !option.Takes(value))
        {
            return steps;
        }

        var label = option.Choices?.FirstOrDefault(c => c.Key == value)?.Label ?? value;
        return [.. steps, new SuiteStep($"{label} ({Catalog.Default.Labels["ServerSettingAfter"]})", new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value }, suite.Videos, suite.Outputs) { Audios = suite.Audios }];
    }

    /// <summary>Returns a suite's runs as the catalog defines them.</summary>
    /// <param name="suite">The suite.</param>
    /// <param name="processorCount">The server's logical CPU count, which bounds the thread limits.</param>
    /// <param name="hardware">The hardware backend, or null to keep every step.</param>
    /// <returns>The runs.</returns>
    private static IReadOnlyList<SuiteStep> CatalogSteps(CatalogSuite suite, int processorCount, HwType? hardware)
    {
        ArgumentNullException.ThrowIfNull(suite);
        if (!suite.ThreadSteps)
        {
            return [.. suite.Steps
                .Where(s => s.Backends is null || hardware is null || s.Backends.Contains(hardware.Value))
                .Select(s => new SuiteStep(s.Label, s.Options, s.Videos ?? suite.Videos, s.Outputs ?? suite.Outputs) { HardwareOnly = s.Backends is not null, Audios = suite.Audios })];
        }

        // Auto is the option's own first choice; limits are labelled from the suite's pattern.
        if (Catalog.Default.Option(ThreadOption) is not { Choices: [var auto, ..] } option)
        {
            throw new InvalidOperationException($"The catalog's {ThreadOption} option is missing or has no choices.");
        }

        // Auto, then doubling thread limits, then the CPU count itself; only limits the setting offers.
        List<int> limits = [];
        for (var n = 1; n < processorCount; n *= 2)
        {
            limits.Add(n);
        }

        limits.Add(processorCount);
        var values = limits.Select(n => n.ToString(CultureInfo.InvariantCulture)).Where(option.Takes).Distinct(StringComparer.Ordinal);
        return [.. new[] { (auto.Key, auto.Label) }.Concat(values.Select(v => (v, (v == "1" ? suite.ThreadLabelOne : suite.ThreadLabel).Replace("{n}", v, StringComparison.Ordinal))))
            .Select(v => new SuiteStep(v.Item2, new Dictionary<string, string>(StringComparer.Ordinal) { [ThreadOption] = v.Item1 }, suite.Videos, suite.Outputs) { Audios = suite.Audios })];
    }
}
