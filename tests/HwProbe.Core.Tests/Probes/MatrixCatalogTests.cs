using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Which cells each backend gets, matching Jellyfin's transcoding settings.</summary>
[Trait("Category", "Unit")]
public sealed class MatrixCatalogTests
{
    private static readonly string[] _decodeKeys =
    [
        "h264", "hevc", "mpeg1video", "mpeg2video", "mpeg4", "vc1", "vp8", "vp9", "av1",
        "hevc_10bit", "vp9_10bit", "hevc_rext_10bit", "hevc_rext_444_10bit", "hevc_rext_12bit", "hevc_rext_422_12bit", "av1_10bit",
    ];

    /// <summary>Every backend but v4l2m2m decodes every option in Jellyfin's list.</summary>
    /// <param name="type">The backend.</param>
    [Theory]
    [InlineData(HwType.vaapi)]
    [InlineData(HwType.qsv)]
    [InlineData(HwType.nvenc)]
    [InlineData(HwType.videotoolbox)]
    public void DecodeCellsMatchJellyfinOptions(HwType type)
    {
        var keys = Keys(type, MatrixGroup.Decode).Where(k => !k.EndsWith("_qsvdecoder", StringComparison.Ordinal) && !k.EndsWith("_cuvid", StringComparison.Ordinal) && k != "h264_keyframes");

        Assert.Equal(_decodeKeys.Order(StringComparer.Ordinal), keys.Order(StringComparer.Ordinal));
    }

    /// <summary>Only QSV repeats the decode cells with "Prefer OS native decoders" off.</summary>
    [Fact]
    public void OnlyQsvTestsQsvDecoders()
    {
        var qsv = MatrixCatalog.For(HwType.qsv).Where(c => c.Key.EndsWith("_qsvdecoder", StringComparison.Ordinal)).ToList();

        Assert.Equal(_decodeKeys.Length, qsv.Count);
        Assert.All(qsv, c => Assert.False(c.Cell.PreferNativeDecoder));
        Assert.DoesNotContain(MatrixCatalog.For(HwType.vaapi), c => !c.Cell.PreferNativeDecoder);
    }

    /// <summary>Intel backends get both low-power encoders and a VPP tone-map cell; others don't.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="intel">Whether it's an Intel path.</param>
    [Theory]
    [InlineData(HwType.qsv, true)]
    [InlineData(HwType.vaapi, true)]
    [InlineData(HwType.nvenc, false)]
    [InlineData(HwType.videotoolbox, false)]
    public void IntelOnlyCells(HwType type, bool intel)
    {
        var encode = Keys(type, MatrixGroup.Encode);
        var tonemap = MatrixCatalog.For(type).Where(c => c.Group == MatrixGroup.Tonemap).ToList();

        Assert.Equal(intel, encode.Contains("h264_lowpower") && encode.Contains("hevc_lowpower"));
        Assert.Equal(intel, tonemap.Any(c => c.Cell.VppTonemap));
    }

    /// <summary>Backends with a hardware MJPEG encoder get a trickplay-sized MJPEG cell; others don't.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="mjpeg">Whether Jellyfin has an MJPEG encoder for it.</param>
    [Theory]
    [InlineData(HwType.qsv, true)]
    [InlineData(HwType.vaapi, true)]
    [InlineData(HwType.videotoolbox, true)]
    [InlineData(HwType.rkmpp, true)]
    [InlineData(HwType.nvenc, false)]
    [InlineData(HwType.amf, false)]
    [InlineData(HwType.v4l2m2m, false)]
    public void MjpegCellForTrickplay(HwType type, bool mjpeg)
    {
        var cell = MatrixCatalog.For(type).SingleOrDefault(c => c.Key == "mjpeg");

        Assert.Equal(mjpeg, cell is not null);
        if (cell is not null)
        {
            Assert.Equal(MatrixGroup.Encode, cell.Group);
            Assert.True(cell.Cell.HardwareDecode);
            Assert.Equal(320, cell.Cell.MaxWidth);
        }
    }

    /// <summary>NVENC tests each decode with the cuvid decoders too, as QSV does with its own decoders.</summary>
    [Fact]
    public void NvencTestsCuvidDecoders()
    {
        var cuvid = MatrixCatalog.For(HwType.nvenc).Where(c => c.Key.EndsWith("_cuvid", StringComparison.Ordinal)).ToList();

        Assert.Contains(cuvid, c => c.Key == "h264_cuvid");
        Assert.All(cuvid, c => Assert.False(c.Cell.EnhancedNvdec));
        Assert.DoesNotContain(MatrixCatalog.For(HwType.qsv), c => !c.Cell.EnhancedNvdec);
    }

    /// <summary>A BWDIF deinterlace cell only where Jellyfin's deinterlacer follows the deinterlacing method.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="bwdif">Whether the method reaches its deinterlacer.</param>
    [Theory]
    [InlineData(HwType.nvenc, true)]
    [InlineData(HwType.amf, true)]
    [InlineData(HwType.videotoolbox, true)]
    [InlineData(HwType.qsv, false)]
    [InlineData(HwType.vaapi, false)]
    public void BwdifCellWhereTheMethodMatters(HwType type, bool bwdif) =>
        Assert.Equal(bwdif, MatrixCatalog.For(type).Any(c => c.Group == MatrixGroup.Deinterlace && c.Cell.Bwdif && c.Cell.Interlaced));

    /// <summary>v4l2m2m only encodes, in each format Jellyfin can pick its encoder for, from a software decode.</summary>
    [Fact]
    public void V4l2EncodesEveryFormat()
    {
        var cells = MatrixCatalog.For(HwType.v4l2m2m);

        Assert.Equal(Catalog.Default.Codecs.Select(c => c.Key), cells.Select(c => c.Key));
        Assert.All(cells, c => Assert.Equal((MatrixGroup.Encode, false), (c.Group, c.Cell.HardwareDecode)));
    }

    /// <summary>Every backend but v4l2m2m gets an interlaced deinterlace cell.</summary>
    [Fact]
    public void DeinterlaceCellIsInterlaced()
    {
        var cell = Assert.Single(MatrixCatalog.For(HwType.vaapi), c => c.Group == MatrixGroup.Deinterlace);

        Assert.True(cell.Cell.Interlaced);
        Assert.True(cell.Fixture.Interlaced);
        Assert.DoesNotContain(MatrixCatalog.For(HwType.v4l2m2m), c => c.Group == MatrixGroup.Deinterlace);
    }

    /// <summary>RExt cells carry the profile and pixel format Jellyfin checks.</summary>
    [Fact]
    public void RextCellsCarryProfileAndPixelFormat()
    {
        var rext12 = Assert.Single(MatrixCatalog.For(HwType.qsv), c => c.Key == "hevc_rext_12bit");

        Assert.Equal(("Rext", "yuv444p12le", 12), (rext12.Cell.Profile, rext12.Cell.PixelFormat, rext12.Cell.BitDepth));
    }

    /// <summary>Returns the keys of one group for a backend.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="group">The group.</param>
    /// <returns>The keys.</returns>
    private static List<string> Keys(HwType type, MatrixGroup group) =>
        [.. MatrixCatalog.For(type).Where(c => c.Group == group).Select(c => c.Key)];
}
