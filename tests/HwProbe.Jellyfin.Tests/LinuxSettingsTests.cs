using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>How the VPP, native-decoder and deinterlace settings change real jellyfin-ffmpeg 8.1.2 args.</summary>
[Trait("Category", "Unit")]
[Collection(TestCollections.EncodingHelperEnvironment)]
public sealed class LinuxSettingsTests
{
    private const string Node = "/tmp/hwprobe-renderD128";
    private const string Skip = "Requires Linux: EncodingHelper only takes these branches there.";
    private const string Amd64 = "jellyfin-8.1.2-linux-amd64";

    /// <summary>On Intel with VPP enabled, Jellyfin tone-maps with VPP rather than OpenCL.</summary>
    /// <param name="type">QSV or VAAPI.</param>
    /// <returns>A task representing the test.</returns>
    [Theory(Skip = Skip, SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    [InlineData(HwType.qsv)]
    [InlineData(HwType.vaapi)]
    public async Task VppTonemapIsUsedOnIntel(HwType type)
    {
        var vpp = Assert.Single(MatrixCatalog.For(type), c => c.Cell.VppTonemap).Cell;

        var args = await BuildAsync(type, vpp);

        Assert.True(args.HardwareTonemap);
    }

    /// <summary>With "Prefer OS native decoders" off, QSV decodes with a different decoder.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact(Skip = Skip, SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public async Task QsvDecoderDiffersFromNative()
    {
        var native = Assert.Single(MatrixCatalog.For(HwType.qsv), c => c.Group == MatrixGroup.Decode && c.Key == "hevc").Cell;
        var qsvDecoder = Assert.Single(MatrixCatalog.For(HwType.qsv), c => c.Group == MatrixGroup.Decode && c.Key == "hevc_qsvdecoder").Cell;

        var nativeArgs = await BuildAsync(HwType.qsv, native);
        var qsvArgs = await BuildAsync(HwType.qsv, qsvDecoder);

        Assert.NotNull(qsvArgs.HardwareDecoder);
        Assert.NotEqual(nativeArgs.HardwareDecoder, qsvArgs.HardwareDecoder);
    }

    /// <summary>Interlaced input is deinterlaced in hardware on the full Intel pipelines.</summary>
    /// <param name="type">QSV or VAAPI.</param>
    /// <returns>A task representing the test.</returns>
    [Theory(Skip = Skip, SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    [InlineData(HwType.qsv)]
    [InlineData(HwType.vaapi)]
    public async Task InterlacedIsDeinterlacedInHardware(HwType type)
    {
        var cell = Assert.Single(MatrixCatalog.For(type), c => c.Group == MatrixGroup.Deinterlace).Cell;

        var args = await BuildAsync(type, cell);

        Assert.NotNull(args.HardwareDeinterlacer);
    }

    /// <summary>Builds args over the recorded amd64 build on an iHD device.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="cell">The cell.</param>
    /// <returns>The args.</returns>
    private static async Task<ProbeArguments> BuildAsync(HwType type, ProbeCell cell)
    {
        await File.WriteAllTextAsync(Node, string.Empty, TestContext.Current.CancellationToken);
        var caps = await CorpusCapabilities.LoadAsync(Amd64, VaapiDriver.IntelIhd);
        return new ArgumentSource(caps, new CallRecorder()).Build(type, Node, cell);
    }
}
