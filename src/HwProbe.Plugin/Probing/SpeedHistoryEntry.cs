namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>One saved speed run, as the page lists it.</summary>
/// <param name="Id">The run's file name, without <c>.json</c>.</param>
/// <param name="GeneratedUtc">When it finished.</param>
/// <param name="Method">How streams were counted.</param>
/// <param name="Tests">How many tests it measured.</param>
/// <param name="Current">Whether it was measured with this HwProbe and ffmpeg.</param>
public sealed record SpeedHistoryEntry(string Id, DateTimeOffset GeneratedUtc, string Method, int Tests, bool Current);
