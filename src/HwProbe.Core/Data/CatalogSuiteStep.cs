namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>One run of a test suite.</summary>
public sealed class CatalogSuiteStep
{
    /// <summary>Gets what the run is called, e.g. <c>medium</c>.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the settings the run sets, by catalog option key.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the videos this run measures instead of the suite's, or null.</summary>
    public IReadOnlyList<string>? Videos { get; init; }

    /// <summary>Gets the outputs this run measures instead of the suite's, or null.</summary>
    public IReadOnlyList<string>? Outputs { get; init; }
}
