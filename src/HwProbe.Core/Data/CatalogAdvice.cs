namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>The thresholds the suggestions, the page, and the CLI judge measurements by.</summary>
public sealed class CatalogAdvice
{
    /// <summary>Gets the share by which runs of the same command vary, under which two speeds count as alike.</summary>
    public required double Noise { get; init; }

    /// <summary>Gets the multiple of real time a better-quality value must keep on real video to be suggested.</summary>
    public required double Headroom { get; init; }

    /// <summary>Gets the share of concurrent streams a better-quality value may cost and still be suggested.</summary>
    public required double MaxStreamLoss { get; init; }

    /// <summary>Gets the share less of a resource that counts as a saving, and more as a cost.</summary>
    public required double ResourceMargin { get; init; }
}
