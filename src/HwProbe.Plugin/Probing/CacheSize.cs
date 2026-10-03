namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>How much the clip cache holds.</summary>
/// <param name="Bytes">Total size.</param>
/// <param name="Files">Number of files.</param>
public sealed record CacheSize(long Bytes, int Files);
