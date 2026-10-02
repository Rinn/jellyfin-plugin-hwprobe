namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>One probe's media shape: what is decoded, what is encoded, and on which side the hardware is used.</summary>
/// <param name="InputCodec">Source codec as ffprobe names it, e.g. <c>h264</c>, <c>hevc</c>, <c>mpeg2video</c>.</param>
/// <param name="BitDepth">Source bit depth.</param>
/// <param name="OutputCodec">Target codec, e.g. <c>h264</c>, <c>hevc</c>, <c>av1</c>.</param>
/// <param name="HardwareDecode">Whether the source codec is enabled for hardware decoding.</param>
/// <param name="HardwareEncode">Whether hardware encoding is enabled.</param>
public sealed record ProbeCell(
    string InputCodec,
    int BitDepth,
    string OutputCodec,
    bool HardwareDecode,
    bool HardwareEncode)
{
    /// <summary>Gets the source profile as ffprobe reports it, e.g. <c>Main 10</c>.</summary>
    public string? Profile { get; init; }

    /// <summary>Gets the stream pixel format, or null for 4:2:0 at <see cref="BitDepth"/>.</summary>
    public string? PixelFormat { get; init; }

    /// <summary>Gets a value indicating whether the stream is interlaced.</summary>
    public bool Interlaced { get; init; }

    /// <summary>Gets a value indicating whether Jellyfin's "Prefer OS native DXVA or VA-API decoders" is on (its default).</summary>
    public bool PreferNativeDecoder { get; init; } = true;

    /// <summary>Gets a value indicating whether Jellyfin's "Enable enhanced NVDEC decoder" is on (its default); off means the cuvid decoders.</summary>
    public bool EnhancedNvdec { get; init; } = true;

    /// <summary>Gets a value indicating whether the deinterlacing method is BWDIF rather than YADIF, Jellyfin's default.</summary>
    public bool Bwdif { get; init; }

    /// <summary>Gets a value indicating whether Intel VPP tone-mapping is enabled; only meaningful with <see cref="Tonemap"/>.</summary>
    public bool VppTonemap { get; init; }

    /// <summary>Gets a text subtitle file to burn in, or null for none.</summary>
    public string? SubtitlePath { get; init; }

    /// <summary>Gets the source colour transfer, e.g. <c>smpte2084</c> for HDR10.</summary>
    public string? ColorTransfer { get; init; }

    /// <summary>Gets the source colour primaries, e.g. <c>bt2020</c>.</summary>
    public string? ColorPrimaries { get; init; }

    /// <summary>Gets the source colour space, e.g. <c>bt2020nc</c>.</summary>
    public string? ColorSpace { get; init; }

    /// <summary>Gets the requested maximum output width, forcing a scale filter; null keeps the source size.</summary>
    public int? MaxWidth { get; init; }

    /// <summary>Gets the requested maximum output height; null keeps the source size.</summary>
    public int? MaxHeight { get; init; }

    /// <summary>Gets a value indicating whether Intel low-power encoding is enabled.</summary>
    public bool LowPower { get; init; }

    /// <summary>Gets a value indicating whether tone-mapping is enabled.</summary>
    public bool Tonemap { get; init; }
}
