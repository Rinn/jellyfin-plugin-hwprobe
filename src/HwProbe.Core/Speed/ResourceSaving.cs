namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>How much less of one resource a result used than another.</summary>
/// <param name="Resource">The resource: <c>Cpu</c>, <c>Memory</c>, <c>Gpu</c>, <c>GpuMemory</c>, or <c>Power</c> (energy per frame above idle).</param>
/// <param name="Fraction">How much less, e.g. 0.3 for 30% less.</param>
public sealed record ResourceSaving(string Resource, double Fraction);
