namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>The Jellyfin settings a speed run starts from; comparisons change one at a time.</summary>
/// <remarks>The defaults are Jellyfin's own (EncodingOptions, v12.1); the plugin passes the server's.</remarks>
public sealed record SpeedSettings
{
    /// <summary>Gets the encoder preset name, or null for <c>auto</c>.</summary>
    public string? EncoderPreset { get; init; }

    /// <summary>Gets a value indicating whether VBR audio encoding is on.</summary>
    public bool AudioVbr { get; init; }

    /// <summary>Gets the H.264 CRF.</summary>
    public int H264Crf { get; init; } = 23;

    /// <summary>Gets the HEVC CRF.</summary>
    public int H265Crf { get; init; } = 28;

    /// <summary>Gets a value indicating whether Intel low-power H.264 encoding is on.</summary>
    public bool LowPowerH264 { get; init; }

    /// <summary>Gets a value indicating whether Intel low-power HEVC encoding is on.</summary>
    public bool LowPowerHevc { get; init; }

    /// <summary>Gets a value indicating whether tone-mapping is on, for HDR files; HwProbe advises it, so it's on unless the server's is off.</summary>
    public bool Tonemap { get; init; } = true;

    /// <summary>Gets a value indicating whether VPP tone-mapping is on.</summary>
    public bool VppTonemap { get; init; }

    /// <summary>Gets a value indicating whether OS native decoders are preferred for QSV.</summary>
    public bool PreferNativeDecoder { get; init; } = true;

    /// <summary>Gets a value indicating whether the enhanced NVDEC decoder is on.</summary>
    public bool EnhancedNvdec { get; init; } = true;

    /// <summary>Gets a value indicating whether deinterlacing doubles the frame rate.</summary>
    public bool DoubleRate { get; init; }

    /// <summary>Gets a value indicating whether the deinterlacer is BWDIF rather than YADIF.</summary>
    public bool Bwdif { get; init; }
}
