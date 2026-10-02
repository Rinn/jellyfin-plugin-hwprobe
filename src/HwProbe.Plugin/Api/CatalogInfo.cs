using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>Everything the page lists: speed videos, outputs and choices, and the labels for backends and results.</summary>
/// <param name="Videos">The speed videos.</param>
/// <param name="Outputs">The speed outputs.</param>
/// <param name="Variations">The speed variations.</param>
/// <param name="Methods">The speed accuracies.</param>
/// <param name="DefaultMethod">The accuracy chosen first.</param>
/// <param name="Repeats">The repeat counts offered.</param>
/// <param name="TimeLimits">The time limits per measurement offered, in seconds.</param>
/// <param name="Backends">The backends, in the order of Jellyfin's dropdown.</param>
/// <param name="Tiers">The pipeline tiers' descriptions.</param>
/// <param name="Verdicts">The descriptions of backends that don't work.</param>
/// <param name="Findings">The descriptions of findings, by code.</param>
/// <param name="Settings">The names of a speed run's starting settings.</param>
public sealed record CatalogInfo(
    IReadOnlyList<SpeedVideoInfo> Videos,
    IReadOnlyList<SpeedOutputInfo> Outputs,
    IReadOnlyList<CatalogVariation> Variations,
    IReadOnlyList<CatalogMethod> Methods,
    SpeedMethod DefaultMethod,
    IReadOnlyList<CatalogOption> Repeats,
    IReadOnlyList<CatalogOption> TimeLimits,
    IReadOnlyList<CatalogBackend> Backends,
    IReadOnlyDictionary<PipelineTier, string> Tiers,
    IReadOnlyDictionary<BackendVerdict, string> Verdicts,
    IReadOnlyDictionary<string, string> Findings,
    IReadOnlyDictionary<string, string> Settings)
{
    /// <summary>Returns the page's view of a catalog.</summary>
    /// <param name="catalog">The catalog.</param>
    /// <returns>The view.</returns>
    public static CatalogInfo From(Catalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new(
            [.. SpeedCatalog.Videos.Select(SpeedVideoInfo.From)],
            [.. SpeedCatalog.Outputs.Select(SpeedOutputInfo.From)],
            catalog.Variations,
            catalog.Methods,
            catalog.DefaultMethod,
            catalog.Repeats,
            catalog.TimeLimits,
            catalog.Backends,
            catalog.Tiers,
            catalog.Verdicts,
            catalog.Findings,
            catalog.Settings);
    }
}
