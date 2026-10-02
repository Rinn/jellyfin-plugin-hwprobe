using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Pipeline;

/// <summary>Predicts whether upstream keeps an NVENC job on the GPU.</summary>
/// <remarks>Mirrors <c>GetNvidiaVidFilterChain</c> in Jellyfin 12.1 <c>EncodingHelper</c>.</remarks>
public static class CudaTier
{
    // EncodingHelper.IsCudaFullSupported (v12.1, L313) plus the alphasrc check in GetNvidiaVidFilterChain.
    private static readonly string[] _requiredFilters = ["yadif_cuda", "overlay_cuda", "hwupload_cuda", "alphasrc"];
    private static readonly string[] _requiredOptions = ["ScaleCudaFormat", "TonemapCudaName"];

    /// <summary>Resolves the NVENC pipeline tier.</summary>
    /// <param name="hwaccel">Whether the build has a hwaccel.</param>
    /// <param name="filter">Whether the build has a filter.</param>
    /// <param name="filterOption">Whether a <see cref="Ffmpeg.FilterOptionCheck"/> passed, by key.</param>
    /// <returns><see cref="PipelineTier.FullCuda"/>, or <see cref="PipelineTier.LegacyCopyBack"/> when anything is missing.</returns>
    public static PipelineTier Resolve(Func<string, bool> hwaccel, Func<string, bool> filter, Func<string, bool> filterOption)
    {
        ArgumentNullException.ThrowIfNull(hwaccel);
        return Missing(filter, filterOption).Count == 0 && hwaccel("cuda") ? PipelineTier.FullCuda : PipelineTier.LegacyCopyBack;
    }

    /// <summary>Lists what the build lacks for the CUDA pipeline.</summary>
    /// <param name="filter">Whether the build has a filter.</param>
    /// <param name="filterOption">Whether a filter option check passed, by key.</param>
    /// <returns>Missing filters, and filters missing an option as <c>filter (option)</c>.</returns>
    public static IReadOnlyList<string> Missing(Func<string, bool> filter, Func<string, bool> filterOption)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(filterOption);
        return
        [
            .. _requiredFilters.Where(f => !filter(f)),
            .. _requiredOptions.Where(o => !filterOption(o)).Select(o => o == "ScaleCudaFormat" ? "scale_cuda (format)" : "tonemap_cuda (tonemap name)"),
        ];
    }
}
