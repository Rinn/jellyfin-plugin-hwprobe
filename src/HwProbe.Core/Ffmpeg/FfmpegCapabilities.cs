using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>What the ffmpeg binary was built with. Build-time facts, not device results.</summary>
/// <param name="Path">The binary that was enumerated.</param>
/// <param name="VersionOutput">Raw <c>-version</c> output.</param>
/// <param name="Version">Parsed version, or null if unidentifiable.</param>
/// <param name="Validation">Upstream's version gate result.</param>
/// <param name="IsJellyfinBuild">Whether this is jellyfin-ffmpeg.</param>
/// <param name="Hwaccels">Parsed <c>-hwaccels</c>.</param>
/// <param name="Encoders">Parsed <c>-encoders</c>.</param>
/// <param name="Decoders">Parsed <c>-decoders</c>.</param>
/// <param name="Filters">Parsed <c>-filters</c>.</param>
/// <param name="FilterOptions">Result per <see cref="FilterOptionCheck.Key"/>.</param>
/// <param name="BuildStatus">Per-backend build status.</param>
public sealed record FfmpegCapabilities(
    string Path,
    string VersionOutput,
    Version? Version,
    FfmpegValidation Validation,
    bool IsJellyfinBuild,
    IReadOnlySet<string> Hwaccels,
    IReadOnlySet<string> Encoders,
    IReadOnlySet<string> Decoders,
    IReadOnlySet<string> Filters,
    IReadOnlyDictionary<string, bool> FilterOptions,
    IReadOnlyDictionary<HwType, BuildStatus> BuildStatus)
{
    /// <summary>Gets the first line of <c>-version</c>, used in the fingerprint.</summary>
    public string VersionLine => VersionOutput.Split('\n', 2)[0].TrimEnd('\r');

    /// <summary>Gets a value indicating whether OpenCL filtering is fully supported; <c>EncodingHelper.IsOpenclFullSupported</c> (v12.1).</summary>
    public bool IsOpenclFullSupported =>
        SupportsHwaccel("opencl")
        && SupportsFilter("scale_opencl")
        && SupportsFilterWithOption("TonemapOpenclBt2390")
        && SupportsFilterWithOption("OverlayOpenclFrameSync");

    /// <summary>Reports whether the build lists an hwaccel; mirrors <c>IMediaEncoder.SupportsHwaccel</c>.</summary>
    /// <param name="hwaccel">The hwaccel name, e.g. <c>vaapi</c>.</param>
    /// <returns>True if listed by <c>-hwaccels</c>.</returns>
    public bool SupportsHwaccel(string hwaccel) => Hwaccels.Contains(hwaccel);

    /// <summary>Reports whether the build has an encoder.</summary>
    /// <param name="encoder">The encoder name.</param>
    /// <returns>True if listed by <c>-encoders</c>.</returns>
    public bool SupportsEncoder(string encoder) => Encoders.Contains(encoder);

    /// <summary>Reports whether the build has a decoder.</summary>
    /// <param name="decoder">The decoder name.</param>
    /// <returns>True if listed by <c>-decoders</c>.</returns>
    public bool SupportsDecoder(string decoder) => Decoders.Contains(decoder);

    /// <summary>Reports whether the build has a filter.</summary>
    /// <param name="filter">The filter name.</param>
    /// <returns>True if listed by <c>-filters</c>.</returns>
    public bool SupportsFilter(string filter) => Filters.Contains(filter);

    /// <summary>Reports whether a filter-option check passed.</summary>
    /// <param name="key">An upstream <c>FilterOptionType</c> member name, e.g. <c>OverlayVaapiFrameSync</c>.</param>
    /// <returns>True if the check passed; false for an unknown key.</returns>
    public bool SupportsFilterWithOption(string key) => FilterOptions.GetValueOrDefault(key);
}
