using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Ffmpeg;

/// <summary>Listing and filter-help parsing over recorded homebrew ffmpeg output.</summary>
[Trait("Category", "Unit")]
public sealed class CapabilityParserTests
{
    private const string Homebrew = "homebrew-9.0.2-macos";

    /// <summary>Encoder rows parse; legend rows are excluded.</summary>
    [Fact]
    public void ParsesEncoders()
    {
        var encoders = CapabilityParser.ParseCodecs(Read("encoders.txt"));

        Assert.Contains("libx264", encoders);
        Assert.Contains("h264_videotoolbox", encoders);
        Assert.DoesNotContain("=", encoders);
        Assert.DoesNotContain("------", encoders);
    }

    /// <summary>Decoder rows parse, including hyphenated names.</summary>
    [Fact]
    public void ParsesDecoders()
    {
        var decoders = CapabilityParser.ParseCodecs(Read("decoders.txt"));

        Assert.Contains("hevc", decoders);
        Assert.Contains("libvpx-vp9", decoders);
    }

    /// <summary>Filter rows parse; legend rows are excluded.</summary>
    [Fact]
    public void ParsesFilters()
    {
        var filters = CapabilityParser.ParseFilters(Read("filters.txt"));

        Assert.Contains("overlay", filters);
        Assert.Contains("scale_vt", filters);
        Assert.DoesNotContain("=", filters);
        Assert.DoesNotContain("scale_opencl", filters);
    }

    /// <summary>The header line is dropped from <c>-hwaccels</c>.</summary>
    [Fact]
    public void ParsesHwaccels() =>
        Assert.Equal(["videotoolbox"], CapabilityParser.ParseHwaccels(Read("hwaccels.txt")));

    /// <summary>The required text is found only in the help for the requested filter: absent text, an unknown-filter response, and another filter's help all fail.</summary>
    /// <param name="file">The recorded help.</param>
    /// <param name="filter">The filter asked about.</param>
    /// <param name="required">The required text.</param>
    /// <param name="expected">Whether the check passes.</param>
    [Theory]
    [InlineData("h-filter-overlay.txt", "overlay", "Action to take when encountering EOF from secondary input", true)]
    [InlineData("h-filter-overlay.txt", "overlay", "bt2390", false)]
    [InlineData("h-filter-tonemap_opencl.txt", "tonemap_opencl", "bt2390", false)]
    [InlineData("h-filter-overlay.txt", "overlay_opencl", "Action to take when encountering EOF from secondary input", false)]
    public void HasFilterOption(string file, string filter, string required, bool expected) =>
        Assert.Equal(expected, CapabilityParser.HasFilterOption(Read(file), filter, required));

    /// <summary>Reads a homebrew corpus file.</summary>
    /// <param name="name">File name.</param>
    /// <returns>The text.</returns>
    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "ffmpeg", Homebrew, name));
}
