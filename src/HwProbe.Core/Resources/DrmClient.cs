namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>One DRM client's engine counters, from its fdinfo.</summary>
/// <param name="Id">The device and client id, e.g. <c>0000:00:02.0/7</c>; descriptors sharing one are the same client.</param>
/// <param name="Nanoseconds">Busy time by engine, for drivers that report it (i915, amdgpu).</param>
/// <param name="Cycles">Busy and total cycles by engine, for drivers that report those instead (xe).</param>
public sealed record DrmClient(string Id, IReadOnlyDictionary<string, long> Nanoseconds, IReadOnlyDictionary<string, (long Busy, long Total)> Cycles)
{
    /// <summary>Gets the client's resident GPU buffers in bytes, across memory regions, or null when the driver doesn't report them.</summary>
    public long? ResidentBytes { get; init; }
}
