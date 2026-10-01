using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>What the ffmpeg build and device support, as <see cref="ProbeMediaEncoder"/> reports it to EncodingHelper.</summary>
/// <remarks>Mapped from build enumeration by ArgumentSourceFactory.</remarks>
public sealed record ProbeCapabilities
{
    /// <summary>Gets the ffmpeg binary path.</summary>
    public string EncoderPath { get; init; } = string.Empty;

    /// <summary>Gets the ffmpeg version.</summary>
    public Version EncoderVersion { get; init; } = new(7, 1);

    /// <summary>Gets the names from <c>-hwaccels</c>.</summary>
    public IReadOnlySet<string> Hwaccels { get; init; } = new HashSet<string>();

    /// <summary>Gets the names from <c>-encoders</c>.</summary>
    public IReadOnlySet<string> Encoders { get; init; } = new HashSet<string>();

    /// <summary>Gets the names from <c>-decoders</c>.</summary>
    public IReadOnlySet<string> Decoders { get; init; } = new HashSet<string>();

    /// <summary>Gets the names from <c>-filters</c>.</summary>
    public IReadOnlySet<string> Filters { get; init; } = new HashSet<string>();

    /// <summary>Gets the filter options confirmed by <c>-h filter=</c>.</summary>
    public IReadOnlySet<FilterOptionType> FilterOptions { get; init; } = new HashSet<FilterOptionType>();

    /// <summary>Gets a value indicating whether the VAAPI device uses the Intel iHD driver.</summary>
    public bool IsVaapiDeviceInteliHD { get; init; }

    /// <summary>Gets a value indicating whether the VAAPI device uses the Intel i965 driver.</summary>
    public bool IsVaapiDeviceInteli965 { get; init; }

    /// <summary>Gets a value indicating whether the VAAPI device is AMD.</summary>
    public bool IsVaapiDeviceAmd { get; init; }

    /// <summary>Gets a value indicating whether the VAAPI device supports Vulkan DRM interop.</summary>
    public bool IsVaapiDeviceSupportVulkanDrmInterop { get; init; }

    /// <summary>Gets a value indicating whether the VAAPI device supports Vulkan DRM format modifiers.</summary>
    public bool IsVaapiDeviceSupportVulkanDrmModifier { get; init; }

    /// <summary>Gets a value indicating whether VideoToolbox can decode AV1.</summary>
    public bool IsVideoToolboxAv1DecodeAvailable { get; init; }
}
