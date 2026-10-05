using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>The smoke probe and the codec matrix, built from the fixture catalog.</summary>
public static class MatrixCatalog
{
    /// <summary>Frames decoded per probe.</summary>
    public const int Frames = 10;

    private const string H264 = "h264";

    /// <summary>The bitrate low-power encodes are probed at: a common player quality, so the rate control matches a real transcode's.</summary>
    private const int LowPowerBitrate = 8_000_000;

    /// <summary>Gets the smoke probe: H.264 8-bit decode, hardware scale, H.264 encode.</summary>
    public static MatrixCell Smoke { get; } =
        new(MatrixGroup.Smoke, H264, FixtureCatalog.H264, Cell(FixtureCatalog.H264, H264, hardwareDecode: true) with { MaxWidth = 320, MaxHeight = 240 });

    /// <summary>Gets the software tests: the settings Jellyfin's Transcoding page and Trickplay page still apply with no hardware backend.</summary>
    /// <remarks>Software deinterlacing takes the method as set, with no check that the build has the filter (EncodingHelper.GetSwDeinterlaceFilter).</remarks>
    public static IReadOnlyList<MatrixCell> Software { get; } =
    [
        new(MatrixGroup.Deinterlace, "bwdif", FixtureCatalog.H264Interlaced, Cell(FixtureCatalog.H264Interlaced, H264, hardwareDecode: false) with { HardwareEncode = false, Bwdif = true }),
        new(MatrixGroup.Decode, FixtureCatalog.H264KeyFrames.Key!, FixtureCatalog.H264KeyFrames, Cell(FixtureCatalog.H264KeyFrames, H264, hardwareDecode: false) with { HardwareEncode = false, KeyFramesOnly = true }),
    ];

    /// <summary>Returns the codec matrix cells for a backend.</summary>
    /// <param name="type">The backend.</param>
    /// <returns>Decode, encode and tone-map cells.</returns>
    public static IReadOnlyList<MatrixCell> For(HwType type)
    {
        if (type == HwType.v4l2m2m)
        {
            // Encoder-only upstream: software decode, then the v4l2m2m encoder. The HEVC and AV1 ones back
            // "Allow encoding in HEVC/AV1 format" (EncodingHelper.GetH26xOrAv1Encoder maps v4l2m2m too).
            return [Encode(FixtureCatalog.H264, H264, hardwareDecode: false), Encode(FixtureCatalog.H264, "hevc", hardwareDecode: false), Encode(FixtureCatalog.H264, "av1", hardwareDecode: false)];
        }

        // One decode cell per Jellyfin "Enable hardware decoding for" option (EncodingOptions.HardwareDecodingCodecs
        // and the EnableDecodingColorDepth* switches), plus 10-bit AV1, which the AV1 option also covers, and the RExt
        // formats each RExt option covers.
        FixtureSpec[] decoded =
        [
            FixtureCatalog.H264, FixtureCatalog.Hevc, FixtureCatalog.Mpeg1, FixtureCatalog.Mpeg2, FixtureCatalog.Mpeg4, FixtureCatalog.Vc1, FixtureCatalog.Vp8, FixtureCatalog.Vp9,
            FixtureCatalog.Av1, FixtureCatalog.Hevc10, FixtureCatalog.Vp910, FixtureCatalog.HevcRext10, FixtureCatalog.HevcRext10Yuv444, FixtureCatalog.HevcRext12, FixtureCatalog.HevcRext12Yuv422, FixtureCatalog.Av110,
        ];

        List<MatrixCell> cells =
        [
            .. decoded.Select(f => new MatrixCell(MatrixGroup.Decode, Key(f), f, Cell(f, H264, hardwareDecode: true))),
            Encode(FixtureCatalog.H264, H264, hardwareDecode: true),
            Encode(FixtureCatalog.H264, "hevc", hardwareDecode: true),
            Encode(FixtureCatalog.Hevc10, "hevc", hardwareDecode: true),
            Encode(FixtureCatalog.H264, "av1", hardwareDecode: true),
            new(MatrixGroup.Tonemap, Key(FixtureCatalog.Hdr10), FixtureCatalog.Hdr10, Cell(FixtureCatalog.Hdr10, H264, hardwareDecode: true) with { Tonemap = true }),
            new(MatrixGroup.Deinterlace, "interlaced", FixtureCatalog.H264Interlaced, Cell(FixtureCatalog.H264Interlaced, H264, hardwareDecode: true)),
            new(MatrixGroup.Subtitles, "text", FixtureCatalog.H264, Cell(FixtureCatalog.H264, H264, hardwareDecode: true)) { SubtitleFixture = FixtureCatalog.SubtitlesAss },
        ];

        if (type == HwType.qsv)
        {
            // "Prefer OS native DXVA or VA-API decoders" only changes QSV: off means Intel's QSV decoders.
            cells.AddRange(decoded.Select(f => new MatrixCell(MatrixGroup.Decode, Key(f) + "_qsvdecoder", f, Cell(f, H264, hardwareDecode: true) with { PreferNativeDecoder = false })));
        }

        // Trickplay's "Only generate images from key frames" skips non-key frames in the hardware decoder.
        cells.Add(new(MatrixGroup.Decode, FixtureCatalog.H264KeyFrames.Key!, FixtureCatalog.H264KeyFrames, Cell(FixtureCatalog.H264KeyFrames, H264, hardwareDecode: true) with { KeyFramesOnly = true }));

        if (type == HwType.nvenc)
        {
            // "Enable enhanced NVDEC decoder" only changes NVENC: off means the cuvid decoders.
            cells.AddRange(decoded.Select(f => new MatrixCell(MatrixGroup.Decode, Key(f) + "_cuvid", f, Cell(f, H264, hardwareDecode: true) with { EnhancedNvdec = false })));
        }

        if (type is HwType.nvenc or HwType.amf or HwType.videotoolbox)
        {
            // "Deinterlacing method" only reaches the CUDA, OpenCL and VideoToolbox deinterlacers (EncodingHelper.GetHwDeinterlaceFilter).
            cells.Add(new(MatrixGroup.Deinterlace, "bwdif", FixtureCatalog.H264Interlaced, Cell(FixtureCatalog.H264Interlaced, H264, hardwareDecode: true) with { Bwdif = true }));
        }

        if (type is HwType.qsv or HwType.vaapi)
        {
            // Jellyfin's "Enable VPP Tone mapping", Intel only; it falls back to OpenCL when VPP can't be used.
            cells.Add(new(MatrixGroup.Tonemap, "vpp", FixtureCatalog.Hdr10, Cell(FixtureCatalog.Hdr10, H264, hardwareDecode: true) with { Tonemap = true, VppTonemap = true }));

            // Jellyfin's two Intel Low-Power encoder options; they fail on specific Intel generations.
            foreach (var output in new[] { H264, "hevc" })
            {
                var lowPower = Encode(FixtureCatalog.H264, output, hardwareDecode: true);

                // With the bitrate Jellyfin encodes to: low-power bitrate control requires HuC (intel/media-driver README), so on a Gen 9 NAS without it low power held without a bitrate and was dropped with one (qsvenc falls back, jellyfin-ffmpeg patch 0071).
                cells.Add(lowPower with { Key = output + "_lowpower", Cell = lowPower.Cell with { LowPower = true, FullQuality = true, VideoBitrate = LowPowerBitrate } });
            }
        }

        if (type is HwType.qsv or HwType.vaapi or HwType.videotoolbox or HwType.rkmpp)
        {
            // Trickplay's MJPEG encoding; EncodingHelper's _mjpegCodecMap has encoders for these backends only.
            var mjpeg = Encode(FixtureCatalog.H264, "mjpeg", hardwareDecode: true);
            cells.Add(mjpeg with { Cell = mjpeg.Cell with { MaxWidth = 320 } });
        }

        return cells;
    }

    /// <summary>Report key for a codec, bit depth and profile.</summary>
    /// <param name="codec">The codec.</param>
    /// <param name="bitDepth">The bit depth.</param>
    /// <param name="profile">The profile, e.g. <c>Rext</c>, or null.</param>
    /// <returns>The key.</returns>
    internal static string Key(string codec, int bitDepth, string? profile = null)
    {
        var key = profile switch
        {
            null => codec,
            "Rext" => $"{codec}_rext",
            _ => $"{codec}_{profile}",
        };
        return bitDepth > 8 ? $"{key}_{bitDepth.ToString(CultureInfo.InvariantCulture)}bit" : key;
    }

    /// <summary>Report key for a fixture, e.g. <c>hevc_rext_12bit</c>; 8-bit 4:2:0 is the bare codec.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>The key.</returns>
    private static string Key(FixtureSpec fixture) => fixture.Key ?? Key(fixture.Codec, fixture.BitDepth, fixture.Profile);

    /// <summary>An encode cell from a fixture to an output codec at the fixture's bit depth.</summary>
    /// <param name="fixture">The input fixture.</param>
    /// <param name="output">The output codec.</param>
    /// <param name="hardwareDecode">Whether the input is decoded in hardware.</param>
    /// <returns>The cell.</returns>
    private static MatrixCell Encode(FixtureSpec fixture, string output, bool hardwareDecode) =>
        new(MatrixGroup.Encode, Key(output, fixture.BitDepth), fixture, Cell(fixture, output, hardwareDecode));

    /// <summary>A probe cell describing a fixture, with hardware encode.</summary>
    /// <param name="fixture">The input fixture.</param>
    /// <param name="output">The output codec.</param>
    /// <param name="hardwareDecode">Whether the input is decoded in hardware.</param>
    /// <returns>The probe cell.</returns>
    private static ProbeCell Cell(FixtureSpec fixture, string output, bool hardwareDecode)
    {
        var color = fixture.IsHdr10 ? ColorMetadata.Hdr10 : null;
        return new ProbeCell(fixture.Codec, fixture.BitDepth, output, hardwareDecode, HardwareEncode: true)
        {
            Profile = fixture.Profile,
            PixelFormat = fixture.PixelFormat,
            Interlaced = fixture.Interlaced,
            ColorPrimaries = color?.Primaries,
            ColorTransfer = color?.Transfer,
            ColorSpace = color?.Space,
        };
    }
}
