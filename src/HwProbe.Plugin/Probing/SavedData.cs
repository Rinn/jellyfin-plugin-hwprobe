namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>What performance tests saved besides the clip cache.</summary>
/// <param name="History">The saved runs.</param>
/// <param name="Measurements">The measurements saved for reuse.</param>
public sealed record SavedData(SavedFilesSize History, SavedFilesSize Measurements);
