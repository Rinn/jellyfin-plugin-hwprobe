using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Ffmpeg;

/// <summary>Parsing of <c>-progress pipe:1</c> output.</summary>
[Trait("Category", "Unit")]
public sealed class ProgressParserTests
{
    /// <summary>The last frame value across progress blocks wins; CRLF and surrounding spaces still parse; no frame line, or an unparseable one, yields null rather than zero.</summary>
    /// <param name="progress">Captured stdout.</param>
    /// <param name="expected">The frame count, or null.</param>
    [Theory]
    [InlineData("frame=3\nprogress=continue\nframe=25\nprogress=end\n", 25L)]
    [InlineData("frame= 7 \r\nprogress=end\r\n", 7L)]
    [InlineData("", null)]
    [InlineData("progress=end\n", null)]
    [InlineData("frame=N/A\n", null)]
    [InlineData("keyframe=3\n", null)]
    public void LastFrame(string progress, long? expected) => Assert.Equal(expected, ProgressParser.LastFrame(progress));
}
