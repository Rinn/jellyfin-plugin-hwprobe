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

    /// <summary>Gets the audio input keys it measures.</summary>
    public IReadOnlyList<string> Audios { get; init; } = [];

    /// <summary>Returns how many video measurements the step makes: every video with every video output, on each backend it runs on.</summary>
    /// <param name="backends">The suite's backends.</param>
    /// <returns>The count.</returns>
    public int VideoMeasurements(IReadOnlyCollection<Model.HwType> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        return Videos.Count * Outputs.Count(o => SpeedCatalog.FindOutput(o)?.Audio != true) * backends.Count(b => !HardwareOnly || b != Model.HwType.none);
    }

    /// <summary>Returns how many audio measurements the step makes: every audio input with every audio output, in software when it runs there.</summary>
    /// <param name="backends">The suite's backends.</param>
    /// <returns>The count.</returns>
    public int AudioMeasurements(IReadOnlyCollection<Model.HwType> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        return backends.Contains(Model.HwType.none) && !HardwareOnly ? Audios.Count * Outputs.Count(o => SpeedCatalog.FindOutput(o)?.Audio == true) : 0;
    }
}
