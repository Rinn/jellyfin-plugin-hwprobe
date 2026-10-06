using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;

/// <summary>Hardware frame formats and confirmation strings per backend.</summary>
[Trait("Category", "Unit")]
public sealed class StderrMarkersTests
{
    /// <summary>Each backend maps to upstream's hwaccel output format.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="expected">The format, or null.</param>
    [Theory]
    [InlineData(HwType.vaapi, "vaapi")]
    [InlineData(HwType.qsv, "qsv")]
    [InlineData(HwType.nvenc, "cuda")]
    [InlineData(HwType.amf, "d3d11")]
    [InlineData(HwType.videotoolbox, "videotoolbox_vld")]
    [InlineData(HwType.rkmpp, "drm_prime")]
    [InlineData(HwType.v4l2m2m, null)]
    [InlineData(HwType.none, null)]
    public void HardwareFormatPerBackend(HwType type, string? expected) =>
        Assert.Equal(expected, StderrMarkers.HardwareFormat(type));

    /// <summary>Confirmation strings cover get_format, the decoder line and the filter-graph line; v4l2m2m, encoder-only, is confirmed by its device open.</summary>
    [Fact]
    public void ConfirmationsCoverDecoderAndGraph()
    {
        Assert.Equal(["Format videotoolbox_vld chosen by get_format()", "pix_fmt: videotoolbox_vld", "pixfmt:videotoolbox_vld"], StderrMarkers.HardwareFrames(HwType.videotoolbox));
        Assert.Equal([StderrMarkers.V4l2Device], StderrMarkers.HardwareFrames(HwType.v4l2m2m));
    }

    /// <summary>QSV decoded through VAAPI is confirmed by VAAPI frames, and QSV frames still count.</summary>
    [Fact]
    public void QsvOverVaapiAcceptsVaapiFrames()
    {
        var confirmations = StderrMarkers.HardwareFrames(HwType.qsv, "vaapi");

        Assert.Contains("Format vaapi chosen by get_format()", confirmations);
        Assert.Contains("Format qsv chosen by get_format()", confirmations);
        Assert.Equal(StderrMarkers.HardwareFrames(HwType.qsv), StderrMarkers.HardwareFrames(HwType.qsv, null));
    }

    /// <summary>Each upstream hwaccel maps to the output format it is paired with.</summary>
    /// <param name="hwaccel">The <c>-hwaccel</c> value.</param>
    /// <param name="expected">The frame format.</param>
    [Theory]
    [InlineData("vaapi", "vaapi")]
    [InlineData("d3d11va", "d3d11")]
    [InlineData("videotoolbox", "videotoolbox_vld")]
    [InlineData("rkmpp", "drm_prime")]
    [InlineData("auto", null)]
    public void HwaccelFormatPerHwaccel(string hwaccel, string? expected) =>
        Assert.Equal(expected, StderrMarkers.HwaccelFormat(hwaccel));

    /// <summary>An out-of-range backend is rejected.</summary>
    [Fact]
    public void UnknownTypeThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StderrMarkers.HardwareFormat((HwType)99));
}
