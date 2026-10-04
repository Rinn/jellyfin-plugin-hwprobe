namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A test suite: performance runs that differ in one setting, compared by the suggestions.</summary>
public sealed class CatalogSuite
{
    /// <summary>Gets the key, e.g. <c>presets</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the page calls it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what it finds out.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the video keys each step measures, unless the step names its own.</summary>
    public required IReadOnlyList<string> Videos { get; init; }

    /// <summary>Gets the output keys each step measures, unless the step names its own.</summary>
    public required IReadOnlyList<string> Outputs { get; init; }

    /// <summary>Gets which backends it runs on: <c>configuredAndSoftware</c>, <c>configured</c>, or <c>software</c>.</summary>
    public string Backends { get; init; } = "configuredAndSoftware";

    /// <summary>Gets advice shown with the suite and its results, for what no setting comparison can suggest, or null.</summary>
    public string? Note { get; init; }

    /// <summary>Gets what the server needs for the suite to be offered, e.g. <c>lowPower</c>, or null.</summary>
    public string? Requires { get; init; }

    /// <summary>Gets a value indicating whether the steps are the thread limits Auto, 1, 2, 4 ... up to the server's logical CPU count.</summary>
    public bool ThreadSteps { get; init; }

    /// <summary>Gets how a thread-limit step is labelled, with <c>{n}</c> for the limit.</summary>
    public string ThreadLabel { get; init; } = "{n}";

    /// <summary>Gets how the one-thread step is labelled.</summary>
    public string ThreadLabelOne { get; init; } = "1";

    /// <summary>Gets the settings every step sets besides the one it varies, by catalog option key; a step's own value wins.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the steps, in order.</summary>
    public IReadOnlyList<CatalogSuiteStep> Steps { get; init; } = [];
}
