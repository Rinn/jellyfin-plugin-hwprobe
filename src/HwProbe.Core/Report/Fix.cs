namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>A short action that fixes a problem, with a link to Jellyfin's documentation for it.</summary>
/// <param name="Action">The action, a few words, e.g. <c>Run with --gpus all</c>.</param>
/// <param name="Url">Where the steps are documented, or null.</param>
public sealed record Fix(string Action, Uri? Url);
