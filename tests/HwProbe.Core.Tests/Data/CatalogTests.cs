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
        Assert.Equal("Live-action + CGI", SpeedCatalog.FindVideo("live-action")!.Name);
    }

    /// <summary>Placeholders expand, including ones inside other placeholders, and the fixture builder's {clip:…} is left.</summary>
    [Fact]
    public void ArgumentsExpand()
    {
        var pattern = SpeedCatalog.FindVideo("pattern")!.Fixture!.EncodeArguments;

        Assert.Equal("-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=1920x1080:rate=24 -i {clip:speed_audio_51.mka} -t 10 -c:v libx264 -preset medium -pix_fmt yuv420p -map 0:v -map 1:a -c:a copy", pattern);
    }

    /// <summary>A file that leaves out an enum value, misspells a field or uses an unknown placeholder is refused.</summary>
    /// <param name="find">Text in the compiled-in file.</param>
    /// <param name="replace">What to put instead.</param>
    [Theory]
    [InlineData("  - { key: QsvLowPowerH264,", "  - { key: QsvLowPower264,")]
    [InlineData("  - { key: DoubleRate, label: Double the frame rate when deinterlacing, server: DeinterlaceDoubleRate, switch: true }", "  - { key: DoubleRate2, label: x, switch: true }")]
    [InlineData("  - { key: H265Crf, label: H.265 encoding CRF, server: H265Crf, range: [0, 51] }", "  - { key: H265Crf, label: x, range: [0, 51], switch: true }")]
    [InlineData("  - { type: amf,", "  - { type: amd,")]
    [InlineData("  FullCuda: Full GPU pipeline (CUDA)\n", "")]
    [InlineData("defaultVideos: [pattern]", "defaultVideos: [pattern-8k]")]
    [InlineData("{testSource} -c:v libx264", "{testSourc} -c:v libx264")]
    [InlineData("    name: Test video, H.264", "    nmae: Test video, H.264")]
    [InlineData("sha256: 0da88a6b", "sha256: 0DA88A6B")]
    public void BrokenFileIsRefused(string find, string replace)
    {
        using var reader = new StreamReader(typeof(Catalog).Assembly.GetManifestResourceStream("catalog.yaml")!);
        var yaml = reader.ReadToEnd();
        Assert.Contains(find, yaml, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => Catalog.Parse(yaml.Replace(find, replace, StringComparison.Ordinal)));
    }
}
