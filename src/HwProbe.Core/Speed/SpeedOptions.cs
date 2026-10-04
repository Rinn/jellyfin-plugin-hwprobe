namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What one speed run measures: every chosen output from every chosen video.</summary>
/// <param name="Method">How streams are counted.</param>
/// <param name="Videos">Keys from <see cref="SpeedCatalog.Videos"/>, and <see cref="SpeedCatalog.LibraryKey"/> for <see cref="File"/>.</param>
/// <param name="Outputs">Keys from <see cref="SpeedCatalog.Outputs"/>.</param>
/// <param name="Settings">The Jellyfin settings to start from.</param>
public sealed record SpeedOptions(SpeedMethod Method, IReadOnlyList<string> Videos, IReadOnlyList<string> Outputs, SpeedSettings Settings)
{
    /// <summary>Gets the backends to measure, with <see cref="Model.HwType.none"/> for software, or null for every working backend and software.</summary>
    public IReadOnlyList<Model.HwType>? Backends { get; init; }

    /// <summary>Gets how many times each measurement runs; more than once reports the median.</summary>
    public int Repeats { get; init; } = 1;

    /// <summary>Gets how long each measurement, repeats included, may take before it reports what it has, or null for no limit.</summary>
    public TimeSpan? TimeLimit { get; init; }

    /// <summary>Gets what pauses the run between measurements, or null when it can't be paused.</summary>
    public SpeedPause? Pause { get; init; }

    /// <summary>Gets a value indicating whether a measurement saved by an earlier run with exactly the same settings is reused rather than measured again.</summary>
    public bool ReuseResults { get; init; }

    /// <summary>Gets a value indicating whether each measurement's single copy is measured for CPU, memory, and GPU usage.</summary>
    public bool MeasureResources { get; init; }

    /// <summary>Gets the backends and output codecs whose low-power encoder the probe found missing; a test asking for one isn't measured.</summary>
    public IReadOnlySet<(Model.HwType Type, string Device, string Codec)> LowPowerUnsupported { get; init; } = new HashSet<(Model.HwType, string, string)>();

    /// <summary>Gets the decodes a probe found failing, by backend, device, and decode test (e.g. <c>av1_10bit</c>), which runs decode in software, as Jellyfin does once that codec is left unticked.</summary>
    public IReadOnlySet<(Model.HwType Type, string Device, string Decode)> DecodeUnsupported { get; init; } = new HashSet<(Model.HwType, string, string)>();

    /// <summary>Gets the library file the <c>library</c> video reads, or null.</summary>
    public SpeedFile? File { get; init; }

    /// <summary>Returns the tests to run, video by video, in the order asked.</summary>
    /// <returns>The tests; unknown keys are left out.</returns>
    public IReadOnlyList<SpeedTest> Resolve()
    {
        var videos = Videos.Select(k => k == SpeedCatalog.LibraryKey ? (File is null ? null : SpeedCatalog.LibraryVideo(File)) : SpeedCatalog.FindVideo(k)).OfType<SpeedVideo>().ToList();
        var outputs = Outputs.Select(SpeedCatalog.FindOutput).OfType<SpeedOutput>().ToList();
        return [.. videos.SelectMany(v => outputs.Select(o => SpeedCatalog.Test(v, o)))];
    }

    /// <summary>Returns these options with what a probe found missing: low-power encoders and decodes that fail.</summary>
    /// <param name="report">The probe's report.</param>
    /// <returns>The options.</returns>
    public SpeedOptions ForReport(Report.CapabilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return this with
        {
            LowPowerUnsupported = MissingLowPower(report),

            // Only decodes that ran and failed: untested and skipped ones weren't shown to fail, and Jellyfin decides NotUsed itself.
            DecodeUnsupported = report.Backends.SelectMany(b => b.Decode.Where(d => d.Value is not (Model.ProbeOutcome.Pass or Model.ProbeOutcome.NotUsed or Model.ProbeOutcome.Untested or Model.ProbeOutcome.Skipped)).Select(d => (b.Type, b.Device, d.Key))).ToHashSet(),
        };
    }

    /// <summary>Returns the low-power encoders a probe found missing, by backend, device, and output codec.</summary>
    /// <param name="report">The probe's report.</param>
    /// <returns>Every backend and codec whose <c>_lowpower</c> encode test didn't pass.</returns>
    public static IReadOnlySet<(Model.HwType Type, string Device, string Codec)> MissingLowPower(Report.CapabilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        const string Suffix = "_lowpower";
        return report.Backends
            .SelectMany(b => b.Encode.Where(e => e.Key.EndsWith(Suffix, StringComparison.Ordinal) && e.Value != Model.ProbeOutcome.Pass).Select(e => (b.Type, b.Device, e.Key[..^Suffix.Length])))
            .ToHashSet();
    }
}
