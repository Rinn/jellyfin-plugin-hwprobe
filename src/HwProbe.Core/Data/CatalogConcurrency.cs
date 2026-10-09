namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>How much memory and CPU counting concurrent streams leaves the server, so the copies can't exhaust either.</summary>
public sealed class CatalogConcurrency
{
    /// <summary>Gets the share of the server's memory kept free.</summary>
    public required double MemoryReserveShare { get; init; }

    /// <summary>Gets the least memory kept free, in MiB.</summary>
    public required int MemoryReserveMinimumMiB { get; init; }

    /// <summary>Gets the multiple of one copy's measured memory each copy is counted as.</summary>
    public required double MemoryMargin { get; init; }

    /// <summary>Gets the share of the memory reserve under which running copies are stopped.</summary>
    public required double MemoryStopShare { get; init; }

    /// <summary>Gets the share of the server's CPU the copies may need at real time.</summary>
    public required double CpuShare { get; init; }
}
