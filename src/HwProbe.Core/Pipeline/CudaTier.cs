using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Pipeline;

/// <summary>Predicts whether upstream keeps an NVENC job on the GPU.</summary>
/// <remarks>Mirrors <c>GetNvidiaVidFilterChain</c> in Jellyfin 12.1 <c>EncodingHelper</c>.</remarks>
public static class CudaTier
{
    // EncodingHelper.IsCudaFullSupported (v12.1, L313) in its check order, then the alphasrc check in
    // GetNvidiaVidFilterChain. Only alphasrc can be missing on a working device: without the rest,
    // GetInputVideoHwaccelArgs gives NVENC no hardware arguments, so its smoke test never passes.
    private static readonly (string Name, bool IsOption)[] _required =
    [
        ("ScaleCudaFormat", true), ("yadif_cuda", false), ("TonemapCudaName", true), ("overlay_cuda", false), ("hwupload_cuda", false), ("alphasrc", false),
    ];

    /// <summary>Resolves the NVENC pipeline tier.</summary>
    /// <param name="hwaccel">Whether the build has a hwaccel.</param>
    /// <param name="filter">Whether the build has a filter.</param>
    /// <param name="filterOption">Whether a <see cref="Ffmpeg.FilterOptionCheck"/> passed, by key.</param>
    /// <returns><see cref="PipelineTier.FullCuda"/>, or <see cref="PipelineTier.LegacyCopyBack"/> when anything is missing.</returns>
    public static PipelineTier Resolve(Func<string, bool> hwaccel, Func<string, bool> filter, Func<string, bool> filterOption)
    {
        return Missing(hwaccel, filter, filterOption).Count == 0 ? PipelineTier.FullCuda : PipelineTier.LegacyCopyBack;
    }

    /// <summary>Lists what the build lacks for the CUDA pipeline.</summary>
    /// <param name="hwaccel">Whether the build has a hwaccel.</param>
    /// <param name="filter">Whether the build has a filter.</param>
    /// <param name="filterOption">Whether a filter option check passed, by key.</param>
    /// <returns>The hwaccel, missing filters, and filters missing an option as <c>filter (option)</c>.</returns>
    public static IReadOnlyList<string> Missing(Func<string, bool> hwaccel, Func<string, bool> filter, Func<string, bool> filterOption)
    {
        ArgumentNullException.ThrowIfNull(hwaccel);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(filterOption);
        return
        [
            .. hwaccel("cuda") ? [] : new[] { "the cuda hwaccel" },
            .. _required.Where(r => r.IsOption ? !filterOption(r.Name) : !filter(r.Name)).Select(r => r.Name switch
            {
                "ScaleCudaFormat" => "scale_cuda (format)",
                "TonemapCudaName" => "tonemap_cuda (tonemap name)",
                var name => name,
            }),
        ];
    }
}
