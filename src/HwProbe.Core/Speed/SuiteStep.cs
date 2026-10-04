namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>One run of a test suite, ready to measure.</summary>
/// <param name="Label">What the run is called, e.g. <c>medium</c>.</param>
/// <param name="Options">The settings it sets, by catalog option key.</param>
/// <param name="Videos">The video keys it measures.</param>
/// <param name="Outputs">The output keys it measures.</param>
public sealed record SuiteStep(string Label, IReadOnlyDictionary<string, string> Options, IReadOnlyList<string> Videos, IReadOnlyList<string> Outputs)
{
    /// <summary>Gets a value indicating whether the step is for hardware backends alone, such as a vendor's tone mapping, so software doesn't run it again.</summary>
    public bool HardwareOnly { get; init; }
}
