using System.Text.Json.Serialization;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Every speed result from one run, as written to JSON.</summary>
/// <param name="GeneratedUtc">When the run finished.</param>
/// <param name="Ffmpeg">The ffmpeg measured.</param>
/// <param name="Method">How streams were counted.</param>
/// <param name="Results">One row per backend, test and variant.</param>
public sealed record SpeedReport(DateTimeOffset GeneratedUtc, FfmpegSummary Ffmpeg, SpeedMethod Method, IReadOnlyList<SpeedResult> Results)
{
    /// <summary>Gets the Jellyfin settings the run started from, or null in reports from before they were recorded.</summary>
    public SpeedSettings? Settings { get; init; }

    /// <summary>Gets how many times each measurement ran.</summary>
    public int Repeats { get; init; } = 1;

    /// <summary>Gets a value indicating whether the run was cancelled, so its results are the ones finished before then.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Cancelled { get; init; }

    /// <summary>Gets a value indicating whether it was cancelled because the server started transcoding.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CancelledForTranscode { get; init; }

    /// <summary>Gets the version of HwProbe that measured it.</summary>
    public string HwProbeVersion { get; init; } = CapabilityReport.CurrentHwProbeVersion;

    /// <summary>Gets the test suite the run belongs to, or null for a run started on its own.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Suite { get; init; }

    /// <summary>Gets the suite step the run was, e.g. <c>medium</c>, or null.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SuiteStep { get; init; }

    /// <summary>Gets when the suite this run belongs to started, shared by its steps' runs, or null.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? SuiteStartedUtc { get; init; }
}
