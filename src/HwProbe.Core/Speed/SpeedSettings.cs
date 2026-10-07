namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>The Jellyfin settings a speed run starts from; comparisons change one at a time.</summary>
/// <remarks>The defaults are Jellyfin's own (EncodingOptions, v12.2); the plugin passes the server's.</remarks>
public sealed record SpeedSettings
{
    /// <summary>Gets the encoder preset name, or null for <c>auto</c>.</summary>
    public string? EncoderPreset { get; init; }

    /// <summary>Gets the subtitles burned in: <c>none</c>, <c>text</c> (ASS) or <c>image</c> (PGS).</summary>
    public string BurnIn { get; init; } = "none";

    /// <summary>Gets a value indicating whether audio is copied, as for a client that plays the source's audio, instead of transcoded to stereo AAC.</summary>
    public bool AudioCopy { get; init; }

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

    /// <summary>Gets Intel low-power H.264 encoding for QSV alone, or null for <see cref="LowPowerH264"/>; other backends keep the server's.</summary>
    public bool? QsvLowPowerH264 { get; init; }

    /// <summary>Gets Intel low-power HEVC encoding for QSV alone, or null for <see cref="LowPowerHevc"/>.</summary>
    public bool? QsvLowPowerHevc { get; init; }

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

    /// <summary>Gets the transcoding thread count; -1 (Jellyfin's default) is automatic.</summary>
    public int EncodingThreadCount { get; init; } = -1;

    /// <summary>Gets a value indicating whether the deinterlacer is BWDIF rather than YADIF.</summary>
    public bool Bwdif { get; init; }

    /// <summary>Gets a value indicating whether VideoToolbox tone mapping is on.</summary>
    public bool VideoToolboxTonemap { get; init; }

    /// <summary>Gets the tone mapping algorithm, as Jellyfin's <c>TonemappingAlgorithm</c> names it.</summary>
    public string TonemapAlgorithm { get; init; } = "bt2390";

    /// <summary>Gets the tone mapping mode.</summary>
    public string TonemapMode { get; init; } = "auto";

    /// <summary>Gets the tone mapping range.</summary>
    public string TonemapRange { get; init; } = "auto";

    /// <summary>Gets the tone mapping desaturation.</summary>
    public double TonemapDesat { get; init; }

    /// <summary>Gets the tone mapping peak.</summary>
    public double TonemapPeak { get; init; } = 100;

    /// <summary>Gets the tone mapping parameter.</summary>
    public double TonemapParam { get; init; }

    /// <summary>Gets the stereo downmix algorithm, as Jellyfin's <c>DownMixStereoAlgorithms</c> names it.</summary>
    public string DownmixAlgorithm { get; init; } = "None";

    /// <summary>Gets the audio boost when downmixing.</summary>
    public double DownmixBoost { get; init; } = 2;

    /// <summary>Gets a value indicating whether trickplay images are encoded with the backend's MJPEG encoder (TrickplayOptions.EnableHwEncoding).</summary>
    public bool TrickplayHwEncoding { get; init; }

    /// <summary>Gets a value indicating whether trickplay images come from key frames only (TrickplayOptions.EnableKeyFrameOnlyExtraction).</summary>
    public bool TrickplayKeyFrames { get; init; }

    /// <summary>Gets the threads trickplay's ffmpeg is given (TrickplayOptions.ProcessThreads); 0 lets ffmpeg pick.</summary>
    public int TrickplayThreads { get; init; } = 1;

    /// <summary>Gets the trickplay images' quality scale, 2 (best) to 31 (TrickplayOptions.Qscale).</summary>
    public int TrickplayQscale { get; init; } = 4;

    /// <summary>Gets the trickplay images' width: the first of TrickplayOptions.WidthResolutions, as each width is made by its own ffmpeg.</summary>
    public int TrickplayWidth { get; init; } = 320;

    /// <summary>Gets the time between trickplay images, in milliseconds (TrickplayOptions.Interval).</summary>
    public int TrickplayInterval { get; init; } = 10000;
}
