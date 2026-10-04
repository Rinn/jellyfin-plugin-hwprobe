using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>Everything the page lists: speed videos, outputs and choices, and the labels for backends and results.</summary>
/// <param name="Videos">The speed videos.</param>
/// <param name="Outputs">The speed outputs: every codec at every quality, and decoding alone.</param>
/// <param name="Codecs">The video codecs Jellyfin transcodes to.</param>
/// <param name="Qualities">The qualities a player offers.</param>
/// <param name="Methods">The speed accuracies.</param>
/// <param name="DefaultMethod">The accuracy chosen first.</param>
/// <param name="Repeats">The repeat counts offered.</param>
/// <param name="Options">The settings a run can be given, in the order of Jellyfin's Transcoding page.</param>
/// <param name="TimeLimits">The time limits per measurement offered, in seconds.</param>
/// <param name="WhenTranscoding">What a run can do when the server starts transcoding.</param>
/// <param name="DefaultWhenTranscoding">What a run does by default when the server starts transcoding.</param>
/// <param name="DefaultMeasureResources">Whether a run measures resource usage by default.</param>
/// <param name="Advice">The thresholds measurements are judged by.</param>
/// <param name="SoftwareName">What the page calls software encoding.</param>
/// <param name="ResourceNames">What the page calls each measured resource, in sentence case.</param>
/// <param name="Backends">The backends, in the order of Jellyfin's dropdown.</param>
/// <param name="GpuEngines">What the page calls each GPU engine.</param>
/// <param name="Links">The links the page points to, by name.</param>
/// <param name="Tiers">The pipeline tiers' descriptions.</param>
/// <param name="Verdicts">The descriptions of backends that don't work.</param>
/// <param name="Findings">The descriptions of findings, by code.</param>
/// <param name="Settings">The names of a speed run's starting settings.</param>
public sealed record CatalogInfo(
    IReadOnlyList<SpeedVideoInfo> Videos,
    IReadOnlyList<SpeedOutputInfo> Outputs,
    IReadOnlyList<CatalogCodec> Codecs,
    IReadOnlyList<CatalogQuality> Qualities,
    IReadOnlyList<CatalogMethod> Methods,
    SpeedMethod DefaultMethod,
    IReadOnlyList<CatalogOption> Repeats,
    IReadOnlyList<CatalogSetting> Options,
    IReadOnlyList<CatalogOption> TimeLimits,
    IReadOnlyList<CatalogTranscodeAction> WhenTranscoding,
    TranscodeAction DefaultWhenTranscoding,
    bool DefaultMeasureResources,
    CatalogAdvice Advice,
    string SoftwareName,
    IReadOnlyDictionary<string, string> ResourceNames,
    IReadOnlyList<CatalogBackend> Backends,
    IReadOnlyDictionary<string, string> GpuEngines,
    IReadOnlyDictionary<string, string> Links,
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
            catalog.Codecs,
            catalog.Qualities,
            catalog.Methods,
            catalog.DefaultMethod,
            catalog.Repeats,
            catalog.Options,
            catalog.TimeLimits,
            catalog.WhenTranscoding,
            catalog.DefaultWhenTranscoding,
            catalog.DefaultMeasureResources,
            catalog.Advice,
            catalog.SoftwareName,
            catalog.ResourceNames,
            catalog.Backends,
            catalog.GpuEngines,
            catalog.Links,
            catalog.Tiers,
            catalog.Verdicts,
            catalog.Findings,
            catalog.Settings);
    }
}
