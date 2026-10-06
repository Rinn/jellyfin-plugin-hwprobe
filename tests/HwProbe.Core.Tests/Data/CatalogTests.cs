using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Data;

/// <summary>Reading and checking catalog.yaml in <see cref="Catalog"/>.</summary>
[Trait("Category", "Unit")]
public sealed class CatalogTests
{
    /// <summary>The compiled-in catalog parses and lists Jellyfin's dropdown in its order.</summary>
    [Fact]
    public void DefaultLoads()
    {
        var catalog = Catalog.Default;

        Assert.Equal([HwType.amf, HwType.nvenc, HwType.qsv, HwType.vaapi, HwType.rkmpp, HwType.videotoolbox, HwType.v4l2m2m], catalog.Backends.Select(b => b.Type));
        Assert.Equal(SpeedMethod.Confirm, catalog.DefaultMethod);
        Assert.Contains(catalog.TimeLimits, o => o.Value is null);
    }

    /// <summary>Placeholders expand, including ones inside other placeholders, and the fixture builder's {clip:…} is left.</summary>
    [Fact]
    public void ArgumentsExpand()
    {
        var pattern = SpeedCatalog.FindVideo("pattern")!.Fixture!.EncodeArguments;

        Assert.Matches(@"^-[^{}]+\{clip:speed_audio_51\.mka\}[^{}]+$", pattern);
    }

    /// <summary>A sample downloaded whole shows its length and size, not as generated; a generated test video says so.</summary>
    [Fact]
    public void OriginNamesDownloadsAndGeneratedVideos()
    {
        Assert.Equal("30.3 s, 2 MB", SpeedCatalog.FindVideo("flv-280p")!.Origin);
        Assert.Equal("Generated, 10 s", SpeedCatalog.FindVideo("pattern")!.Origin);
    }

    /// <summary>A file that leaves out an enum value, misspells a field or uses an unknown placeholder is refused.</summary>
    /// <param name="find">Text in the compiled-in file.</param>
    /// <param name="replace">What to put instead.</param>
    [Theory]
    [InlineData("  - { key: QsvLowPowerH264,", "  - { key: QsvLowPower264,")]
    [InlineData("  - { key: DoubleRate, label: Double the frame rate when deinterlacing, server: DeinterlaceDoubleRate, switch: true, qualityOrder: [\"true\", \"false\"],", "  - { key: DoubleRate2, label: x, switch: true,")]
    [InlineData("  - { key: H265Crf, label: H.265 encoding CRF, server: H265Crf, outputCodec: hevc, range: [0, 51], lowerIsBetter: true,", "  - { key: H265Crf, label: x, range: [0, 51], switch: true,")]
    [InlineData("outputCodec: hevc, range", "outputCodec: h265, range")]
    [InlineData("settings: [DeinterlaceMethod, DoubleRate]", "settings: [DeinterlaceMethod, DoubleRate, Tonemap]")]
    [InlineData("    qualityOrder: [bwdif, yadif]", "    qualityOrder: [bwdif, nnedi]")]
    [InlineData("defaultWhenTranscoding: Pause", "defaultWhenTranscoding: Wait")]
    [InlineData("{ label: medium, options: { EncoderPreset: medium } }", "{ label: medium, options: { EncoderPreset: placebo } }")]
    [InlineData("    backends: software\n", "    backends: gpu\n")]
    [InlineData("outputs: [h264-40mbps,", "outputs: [h264-41mbps,")]
    [InlineData("holderUrl: https://mango.blender.org/", "holderUrl: mango.blender.org")]
    [InlineData("  - { type: amf,", "  - { type: amd,")]
    [InlineData("  FullCuda: Full GPU pipeline (CUDA)\n", "")]
    [InlineData("defaultVideos: [drama]", "defaultVideos: [drama-16k]")]
    [InlineData("{testSource} -c:v libx264", "{testSourc} -c:v libx264")]
    [InlineData("    name: Test video, H.264", "    nmae: Test video, H.264")]
    [InlineData("sha256: 0da88a6b", "sha256: 0DA88A6B")]
    [InlineData("      size: 1719794\n", "")]
    [InlineData("testDelay: 1\n", "testDelay: -1\n")]
    public void BrokenFileIsRefused(string find, string replace)
    {
        using var reader = new StreamReader(typeof(Catalog).Assembly.GetManifestResourceStream("catalog.yaml")!);
        var yaml = reader.ReadToEnd();
        Assert.Contains(find, yaml, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => Catalog.Parse(yaml.Replace(find, replace, StringComparison.Ordinal)));
    }
}
