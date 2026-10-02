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

    /// <summary>Gets a value indicating whether only key frames are decoded, as trickplay's key-frame-only extraction does.</summary>
    public bool KeyFramesOnly { get; init; }

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

    /// <summary>Gets the source width.</summary>
    public int SourceWidth { get; init; } = 640;

    /// <summary>Gets the source height.</summary>
    public int SourceHeight { get; init; } = 360;

    /// <summary>Gets the source frame rate.</summary>
    public float SourceFrameRate { get; init; } = 25;

    /// <summary>Gets the requested video bitrate in bits per second, as a client asks for it, or null for none.</summary>
    public int? VideoBitrate { get; init; }

    /// <summary>Gets a value indicating whether the encoder gets upstream's full quality arguments (preset, bitrate, CRF) and the job its audio.</summary>
    /// <remarks>Probes keep only the low-power flag; speed runs need everything a real transcode passes.</remarks>
    public bool FullQuality { get; init; }

    /// <summary>Gets Jellyfin's encoder preset name, e.g. <c>veryfast</c>, or null for <c>auto</c>.</summary>
    public string? EncoderPreset { get; init; }

    /// <summary>Gets Jellyfin's H.264 CRF; upstream's default is 23.</summary>
    public int H264Crf { get; init; } = 23;

    /// <summary>Gets Jellyfin's HEVC CRF; upstream's default is 28.</summary>
    public int H265Crf { get; init; } = 28;

    /// <summary>Gets a value indicating whether the source has a 5.1 AAC track, transcoded to stereo AAC.</summary>
    public bool Audio { get; init; }

    /// <summary>Gets a value indicating whether Jellyfin's "Enable VBR audio encoding" is on.</summary>
    public bool AudioVbr { get; init; }

    /// <summary>Gets a value indicating whether Jellyfin's "Double the frame rate when deinterlacing" is on.</summary>
    public bool DoubleRate { get; init; }

    /// <summary>Gets the input clip, so upstream can write the whole input argument; null for probes, which add the input themselves.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Gets an external image (PGS) subtitle file to burn in, or null for none.</summary>
    public string? GraphicalSubtitlePath { get; init; }

    /// <summary>Gets the video stream's index in the source.</summary>
    public int VideoIndex { get; init; }

    /// <summary>Gets the audio stream's index in the source.</summary>
    public int AudioIndex { get; init; } = 1;

    /// <summary>Gets the source audio codec.</summary>
    public string AudioCodec { get; init; } = "aac";

    /// <summary>Gets the source audio channel count.</summary>
    public int AudioChannels { get; init; } = 6;

    /// <summary>Gets the index of a subtitle stream inside the source to burn in, or null.</summary>
    public int? InternalSubtitleIndex { get; init; }

    /// <summary>Gets that subtitle stream's codec as Jellyfin names it, e.g. <c>PGSSUB</c>.</summary>
    public string? InternalSubtitleCodec { get; init; }

    /// <summary>Gets the media source ID Jellyfin knows the file by, which keys its extracted subtitles; null for a clip.</summary>
    public string? MediaSourceId { get; init; }

    /// <summary>Gets Jellyfin's "Transcoding thread count"; -1 is its default, automatic.</summary>
    public int EncodingThreadCount { get; init; } = -1;
}
