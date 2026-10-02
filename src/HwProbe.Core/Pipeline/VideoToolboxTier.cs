using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Pipeline;

/// <summary>Resolves the VideoToolbox filter-pipeline tier the way upstream selects it.</summary>
public static class VideoToolboxTier
{
    // EncodingHelper.IsVideoToolboxFullSupported (v12.1, L335) plus the alphasrc check in GetAppleVidFilterChain.
    private static readonly string[] _requiredFilters = ["yadif_videotoolbox", "overlay_videotoolbox", "tonemap_videotoolbox", "scale_vt", "alphasrc"];

    /// <summary>Returns the tier.</summary>
    /// <param name="hwaccel">Reports whether a hwaccel is built.</param>
    /// <param name="filter">Reports whether a filter is built.</param>
    /// <returns><see cref="PipelineTier.FullMetal"/> or <see cref="PipelineTier.LegacyCopyBack"/>.</returns>
    public static PipelineTier Resolve(Func<string, bool> hwaccel, Func<string, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(hwaccel);
        ArgumentNullException.ThrowIfNull(filter);

        return MissingFilters(hwaccel, filter).Count == 0 ? PipelineTier.FullMetal : PipelineTier.LegacyCopyBack;
    }

    /// <summary>Returns the filters the full pipeline needs that this build lacks.</summary>
    /// <param name="hwaccel">Whether the build has a hwaccel.</param>
    /// <param name="filter">Reports whether a filter is built.</param>
    /// <returns>The missing filter names, in upstream's check order.</returns>
    public static IReadOnlyList<string> MissingFilters(Func<string, bool> hwaccel, Func<string, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(hwaccel);
        ArgumentNullException.ThrowIfNull(filter);
        return [.. hwaccel("videotoolbox") ? [] : new[] { "the videotoolbox hwaccel" }, .. _requiredFilters.Where(f => !filter(f))];
    }
}
