using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Ffmpeg;

/// <summary>Version parsing and validation, over <c>-version</c> samples from upstream's own tests.</summary>
[Trait("Category", "Unit")]
public sealed class FfmpegVersionParserTests
{
    /// <summary>Banner, <c>n</c>-prefixed, library-fallback and unidentifiable forms parse as upstream does, and validate against the 4.4 minimum.</summary>
    /// <param name="sample">Corpus file under <c>Corpus/ffmpeg/version</c>.</param>
    /// <param name="expected">Expected version, or null.</param>
    /// <param name="validation">Expected validation.</param>
    [Theory]
    [InlineData("jellyfin-7.0.1.txt", "7.0.1", FfmpegValidation.Valid)]
    [InlineData("btbn-n6.1.1.txt", "6.1.1", FfmpegValidation.Valid)]
    [InlineData("jellyfin-4.4.txt", "4.4", FfmpegValidation.Valid)]
    [InlineData("jellyfin-n4.3.2.txt", "4.3.2", FfmpegValidation.TooOld)]
    [InlineData("git-libs-only.txt", "4.4", FfmpegValidation.Valid)]
    [InlineData("git-old-libs.txt", null, FfmpegValidation.UnknownVersion)]
    public void ParsesAndValidates(string sample, string? expected, FfmpegValidation validation)
    {
        var text = Read(sample);

        Assert.Equal(expected is null ? null : Version.Parse(expected), FfmpegVersionParser.Parse(text));
        Assert.Equal(validation, FfmpegVersionParser.Validate(text));
    }

    /// <summary>A Libav banner is rejected even if a version would parse.</summary>
    [Fact]
    public void RejectsLibav() =>
        Assert.Equal(
            FfmpegValidation.Libav,
            FfmpegVersionParser.Validate("ffmpeg version 12.3, Copyright (c) 2000-2018 the Libav developers\n"));

    /// <summary>Empty output is its own failure, distinct from an unknown version.</summary>
    [Fact]
    public void EmptyOutputIsNoOutput() => Assert.Equal(FfmpegValidation.NoOutput, FfmpegVersionParser.Validate(string.Empty));

    /// <summary>jellyfin-ffmpeg builds are recognised; homebrew and BtbN builds are not.</summary>
    /// <param name="sample">Corpus file relative to <c>Corpus/ffmpeg</c>.</param>
    /// <param name="expected">Whether it is a Jellyfin build.</param>
    [Theory]
    [InlineData("version/jellyfin-7.0.1.txt", true)]
    [InlineData("version/jellyfin-n4.3.2.txt", true)]
    [InlineData("version/btbn-n6.1.1.txt", false)]
    [InlineData("homebrew-9.0.2-macos/version.txt", false)]
    public void DetectsJellyfinBuild(string sample, bool expected) =>
        Assert.Equal(expected, FfmpegVersionParser.IsJellyfinBuild(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "ffmpeg", sample))));

    /// <summary>Reads a version sample.</summary>
    /// <param name="sample">File name under <c>Corpus/ffmpeg/version</c>.</param>
    /// <returns>The sample text.</returns>
    private static string Read(string sample) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "ffmpeg", "version", sample));
}
