using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Ffmpeg;

/// <summary>Parsing of <c>-progress pipe:1</c> output.</summary>
[Trait("Category", "Unit")]
public sealed class ProgressParserTests
{
    /// <summary>The last frame value across multiple progress blocks wins.</summary>
    [Fact]
    public void LastBlockWins() =>
        Assert.Equal(25, ProgressParser.LastFrame("frame=3\nprogress=continue\nframe=25\nprogress=end\n"));

    /// <summary>CRLF line endings and surrounding spaces still parse.</summary>
    [Fact]
    public void ToleratesCrlfAndSpaces() =>
        Assert.Equal(7, ProgressParser.LastFrame("frame= 7 \r\nprogress=end\r\n"));

    /// <summary>No frame line, or an unparseable one, yields null rather than zero.</summary>
    /// <param name="progress">Captured stdout.</param>
    [Theory]
    [InlineData("")]
    [InlineData("progress=end\n")]
    [InlineData("frame=N/A\n")]
    [InlineData("keyframe=3\n")]
    public void NoFrameIsNull(string progress) => Assert.Null(ProgressParser.LastFrame(progress));
}
