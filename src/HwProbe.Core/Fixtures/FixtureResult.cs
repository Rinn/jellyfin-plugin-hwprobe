namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>The state of one fixture after a build.</summary>
/// <param name="Spec">The fixture.</param>
/// <param name="Status">Whether it is usable.</param>
/// <param name="Path">Absolute path when available, otherwise null.</param>
/// <param name="Reason">Why it is not available, otherwise null.</param>
public sealed record FixtureResult(FixtureSpec Spec, FixtureStatus Status, string? Path, string? Reason);
