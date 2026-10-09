namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>The memory the server can use, and how much of it is free.</summary>
/// <param name="Available">Bytes it can still use: the host's available memory, or what its cgroup's limit leaves, whichever is less.</param>
/// <param name="Total">Bytes it can use at most: the host's memory, or its cgroup's limit when lower.</param>
public sealed record MemorySnapshot(long Available, long Total);
