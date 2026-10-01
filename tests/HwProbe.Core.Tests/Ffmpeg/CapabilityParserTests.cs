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

    /// <summary>The required text is found when the help is for the requested filter.</summary>
    [Fact]
    public void FilterOptionPresent() =>
        Assert.True(CapabilityParser.HasFilterOption(
            Read("h-filter-overlay.txt"), "overlay", "Action to take when encountering EOF from secondary input"));

    /// <summary>A real filter whose help lacks the required text fails the check.</summary>
    [Fact]
    public void FilterOptionAbsent() =>
        Assert.False(CapabilityParser.HasFilterOption(Read("h-filter-overlay.txt"), "overlay", "bt2390"));

    /// <summary>An unknown-filter response fails the check.</summary>
    [Fact]
    public void UnknownFilterFails() =>
        Assert.False(CapabilityParser.HasFilterOption(Read("h-filter-tonemap_opencl.txt"), "tonemap_opencl", "bt2390"));

    /// <summary>Help for a different filter must not satisfy the check, even if it contains the required text.</summary>
    [Fact]
    public void WrongFilterHelpFails() =>
        Assert.False(CapabilityParser.HasFilterOption(
            Read("h-filter-overlay.txt"), "overlay_opencl", "Action to take when encountering EOF from secondary input"));

    /// <summary>Reads a homebrew corpus file.</summary>
    /// <param name="name">File name.</param>
    /// <returns>The text.</returns>
    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "ffmpeg", Homebrew, name));
}
