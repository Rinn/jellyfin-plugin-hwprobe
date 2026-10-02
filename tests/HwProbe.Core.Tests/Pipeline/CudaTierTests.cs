using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Pipeline;

/// <summary>NVENC tier selection.</summary>
[Trait("Category", "Unit")]
public sealed class CudaTierTests
{
    private static readonly HashSet<string> _filters = ["yadif_cuda", "overlay_cuda", "hwupload_cuda", "alphasrc"];
    private static readonly HashSet<string> _options = ["ScaleCudaFormat", "TonemapCudaName"];

    /// <summary>Every CUDA filter and option plus the hwaccel keeps the job on the GPU.</summary>
    [Fact]
    public void EverythingIsFullCuda() =>
        Assert.Equal(PipelineTier.FullCuda, CudaTier.Resolve(h => h == "cuda", _filters.Contains, _options.Contains));

    /// <summary>Any single missing filter, option or the hwaccel drops to copy-back, and is named.</summary>
    /// <param name="missing">The absent filter or option, or <c>hwaccel</c>.</param>
    /// <param name="named">How the finding names it, or null when nothing is listed.</param>
    [Theory]
    [InlineData("yadif_cuda", "yadif_cuda")]
    [InlineData("overlay_cuda", "overlay_cuda")]
    [InlineData("hwupload_cuda", "hwupload_cuda")]
    [InlineData("alphasrc", "alphasrc")]
    [InlineData("ScaleCudaFormat", "scale_cuda (format)")]
    [InlineData("TonemapCudaName", "tonemap_cuda (tonemap name)")]
    [InlineData("hwaccel", null)]
    public void AnyGapIsLegacy(string missing, string? named)
    {
        bool Filter(string f) => _filters.Contains(f) && f != missing;
        bool Option(string o) => _options.Contains(o) && o != missing;

        Assert.Equal(PipelineTier.LegacyCopyBack, CudaTier.Resolve(h => h == "cuda" && missing != "hwaccel", Filter, Option));
        Assert.Equal(named is null ? [] : [named], CudaTier.Missing(Filter, Option));
    }
}
