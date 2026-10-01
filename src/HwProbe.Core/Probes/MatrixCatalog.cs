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

    /// <summary>Gets the smoke probe: H.264 8-bit decode, hardware scale, H.264 encode.</summary>
    public static MatrixCell Smoke { get; } =
        new(MatrixGroup.Smoke, H264, FixtureCatalog.H264, Cell(FixtureCatalog.H264, H264, hardwareDecode: true) with { MaxWidth = 320, MaxHeight = 240 });

    /// <summary>Returns the codec matrix cells for a backend.</summary>
    /// <param name="type">The backend.</param>
    /// <returns>Decode, encode and tone-map cells.</returns>
    public static IReadOnlyList<MatrixCell> For(HwType type)
    {
        if (type == HwType.v4l2m2m)
        {
            // Encoder-only upstream: software decode, h264_v4l2m2m encode.
            return [Encode(FixtureCatalog.H264, H264, hardwareDecode: false)];
        }

        List<MatrixCell> cells =
        [
            .. new[] { FixtureCatalog.H264, FixtureCatalog.Hevc, FixtureCatalog.Hevc10, FixtureCatalog.Vp9, FixtureCatalog.Av1, FixtureCatalog.Mpeg2, FixtureCatalog.Vc1 }
                .Select(f => new MatrixCell(MatrixGroup.Decode, Key(f.Codec, f.BitDepth), f, Cell(f, H264, hardwareDecode: true))),
            Encode(FixtureCatalog.H264, H264, hardwareDecode: true),
            Encode(FixtureCatalog.H264, "hevc", hardwareDecode: true),
            Encode(FixtureCatalog.Hevc10, "hevc", hardwareDecode: true),
            Encode(FixtureCatalog.H264, "av1", hardwareDecode: true),
            new(MatrixGroup.Tonemap, Key(FixtureCatalog.Hdr10.Codec, FixtureCatalog.Hdr10.BitDepth), FixtureCatalog.Hdr10, Cell(FixtureCatalog.Hdr10, H264, hardwareDecode: true) with { Tonemap = true }),
        ];

        if (type is HwType.qsv or HwType.vaapi)
        {
            // Independently configurable upstream, and fails on specific Intel generations.
            var lowPower = Encode(FixtureCatalog.H264, H264, hardwareDecode: true);
            cells.Add(lowPower with { Key = lowPower.Key + "_lowpower", Cell = lowPower.Cell with { LowPower = true } });
        }

        return cells;
    }

    /// <summary>Report key for a codec and bit depth, e.g. <c>hevc10</c>; 8-bit has no suffix.</summary>
    /// <param name="codec">The codec.</param>
    /// <param name="bitDepth">The bit depth.</param>
    /// <returns>The key.</returns>
    private static string Key(string codec, int bitDepth) =>
        bitDepth > 8 ? codec + bitDepth.ToString(CultureInfo.InvariantCulture) : codec;

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
            ColorPrimaries = color?.Primaries,
            ColorTransfer = color?.Transfer,
            ColorSpace = color?.Space,
        };
    }
}
