using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>A test suite as the page starts it.</summary>
/// <param name="Key">The suite's catalog key.</param>
public sealed record SuiteRequest(string Key)
{
    /// <summary>Gets a value indicating whether each run's single copy is measured for CPU, memory, and GPU usage.</summary>
    public bool MeasureResources { get; init; } = Catalog.Default.DefaultMeasureResources;

    /// <summary>Gets what the suite does when the server transcodes.</summary>
    public TranscodeAction WhenTranscoding { get; init; } = Catalog.Default.DefaultWhenTranscoding;
}
