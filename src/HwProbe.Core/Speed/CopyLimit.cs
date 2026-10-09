namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>The most copies the server has room for at once, and what limits them.</summary>
/// <param name="Copies">The count, at least 1.</param>
/// <param name="ByCpu">True when the CPU limits them, false when memory does.</param>
public sealed record CopyLimit(int Copies, bool ByCpu);
