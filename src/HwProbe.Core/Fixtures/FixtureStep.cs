namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>One step of making or downloading a clip that isn't cached.</summary>
/// <param name="Spec">The clip.</param>
/// <param name="Action">What's being done.</param>
/// <param name="Done">Bytes downloaded so far; 0 while making.</param>
/// <param name="Total">Bytes to download, or 0 when unknown or making.</param>
public sealed record FixtureStep(FixtureSpec Spec, FixtureAction Action, long Done, long Total);
