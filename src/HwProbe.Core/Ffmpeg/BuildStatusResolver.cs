using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Maps a build's hwaccels and encoders to per-backend <see cref="BuildStatus"/>.</summary>
public static class BuildStatusResolver
{
    private static readonly string[] _amfEncoders = ["h264_amf", "hevc_amf", "av1_amf"];

    /// <summary>Resolves every backend except <see cref="HwType.none"/>.</summary>
    /// <param name="hwaccels">Parsed <c>-hwaccels</c>.</param>
    /// <param name="encoders">Parsed <c>-encoders</c>.</param>
    /// <returns>The status per backend.</returns>
    public static IReadOnlyDictionary<HwType, BuildStatus> Resolve(IReadOnlySet<string> hwaccels, IReadOnlySet<string> encoders)
    {
        ArgumentNullException.ThrowIfNull(hwaccels);
        ArgumentNullException.ThrowIfNull(encoders);

        // Each gate is the SupportsHwaccel check guarding that backend in
        // EncodingHelper.GetInputVideoHwaccelArgs (v12.1); v4l2m2m has no branch there, only an encoder.
        return new Dictionary<HwType, BuildStatus>
        {
            [HwType.vaapi] = Status(hwaccels.Contains("vaapi")),
            [HwType.qsv] = Status(hwaccels.Contains("qsv")),
            [HwType.nvenc] = Status(hwaccels.Contains("cuda")),
            [HwType.amf] = Status(hwaccels.Contains("d3d11va") && _amfEncoders.Any(encoders.Contains)),
            [HwType.videotoolbox] = Status(hwaccels.Contains("videotoolbox")),
            [HwType.rkmpp] = Status(hwaccels.Contains("rkmpp")),
            [HwType.v4l2m2m] = Status(encoders.Contains("h264_v4l2m2m")),
        };
    }

    /// <summary>Converts a gate result to a status.</summary>
    /// <param name="built">Whether the gate passed.</param>
    /// <returns>The status.</returns>
    private static BuildStatus Status(bool built) => built ? BuildStatus.Selectable : BuildStatus.NotBuilt;
}
