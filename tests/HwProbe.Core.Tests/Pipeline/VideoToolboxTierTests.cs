using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Pipeline;

/// <summary>VideoToolbox tier selection.</summary>
[Trait("Category", "Unit")]
public sealed class VideoToolboxTierTests
{
    private static readonly HashSet<string> _all = ["yadif_videotoolbox", "overlay_videotoolbox", "tonemap_videotoolbox", "scale_vt", "alphasrc"];

    /// <summary>Every required filter plus the hwaccel selects the Metal pipeline.</summary>
    [Fact]
    public void AllFiltersIsFullMetal() =>
        Assert.Equal(PipelineTier.FullMetal, VideoToolboxTier.Resolve(h => h == "videotoolbox", _all.Contains));

    /// <summary>Homebrew ffmpeg 9.0.2's filter set falls back to copy-back and names what's missing.</summary>
    [Fact]
    public void HomebrewSetIsLegacy()
    {
        HashSet<string> homebrew = ["yadif_videotoolbox", "scale_vt", "transpose_vt"];

        Assert.Equal(PipelineTier.LegacyCopyBack, VideoToolboxTier.Resolve(h => h == "videotoolbox", homebrew.Contains));
        Assert.Equal(["overlay_videotoolbox", "tonemap_videotoolbox", "alphasrc"], VideoToolboxTier.MissingFilters(h => h == "videotoolbox", homebrew.Contains));
        Assert.Equal(["the videotoolbox hwaccel"], VideoToolboxTier.MissingFilters(_ => false, _all.Contains));
    }

    /// <summary>Any single missing filter, or the hwaccel, drops to copy-back.</summary>
    /// <param name="missing">The absent filter, or <c>hwaccel</c>.</param>
    [Theory]
    [InlineData("yadif_videotoolbox")]
    [InlineData("overlay_videotoolbox")]
    [InlineData("tonemap_videotoolbox")]
    [InlineData("scale_vt")]
    [InlineData("alphasrc")]
    [InlineData("hwaccel")]
    public void AnyGapIsLegacy(string missing) =>
        Assert.Equal(
            PipelineTier.LegacyCopyBack,
            VideoToolboxTier.Resolve(h => h == "videotoolbox" && missing != "hwaccel", f => _all.Contains(f) && f != missing));
}
