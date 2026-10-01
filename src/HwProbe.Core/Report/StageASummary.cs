using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>Build-time capabilities; what Jellyfin's dropdown is based on.</summary>
/// <param name="Hwaccels">The <c>-hwaccels</c> list.</param>
/// <param name="Types">Per-backend build status.</param>
/// <param name="FilterOptions">Filter-option checks by name.</param>
public sealed record StageASummary(
    IReadOnlyList<string> Hwaccels,
    IReadOnlyDictionary<HwType, BuildStatus> Types,
    IReadOnlyDictionary<string, bool> FilterOptions);
