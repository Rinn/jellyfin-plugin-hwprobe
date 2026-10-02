using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Diagnostics;

/// <summary>Path and name replacement in <see cref="DiagnosticsScrubber"/>.</summary>
[Trait("Category", "Unit")]
public sealed class DiagnosticsScrubberTests
{
    private readonly DiagnosticsScrubber _scrubber = new(
        [("/home/alex", "~"), ("/home/alex/.cache/hwprobe/", "<cache>"), (@"C:\Users\Alex", "~")],
        [("alex", "<user>"), ("nas01", "<host>"), ("pi", "<user>")]);

    /// <summary>Paths, JSON-escaped Windows paths and whole-word names are replaced; the longest path wins.</summary>
    /// <param name="text">The input.</param>
    /// <param name="expected">The scrubbed text.</param>
    [Theory]
    [InlineData("-i /home/alex/.cache/hwprobe/fixtures/h264.mp4", "-i <cache>/fixtures/h264.mp4")]
    [InlineData("--ffmpeg /home/alex/bin/ffmpeg", "--ffmpeg ~/bin/ffmpeg")]
    [InlineData(@"C:\users\alex\ffmpeg.exe", @"~\ffmpeg.exe")]
    [InlineData(@"""path"": ""C:\\Users\\Alex\\ffmpeg.exe""", @"""path"": ""~\\ffmpeg.exe""")]
    [InlineData("user Alex on NAS01", "user <user> on <host>")]
    [InlineData("alexander nas012", "alexander nas012")]
    [InlineData("-progress pipe:1 pi", "-progress pipe:1 pi")]
    public void ReplacesIdentifyingText(string text, string expected) => Assert.Equal(expected, _scrubber.Scrub(text));

    /// <summary>The root directory is never treated as a path to replace.</summary>
    [Fact]
    public void RootHomeIsIgnored() =>
        Assert.Equal("/usr/lib/ffmpeg", new DiagnosticsScrubber([("/", "~")], []).Scrub("/usr/lib/ffmpeg"));
}
