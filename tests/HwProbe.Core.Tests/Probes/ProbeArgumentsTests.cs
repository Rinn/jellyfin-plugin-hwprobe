using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Reading the hwaccel out of generated arguments.</summary>
[Trait("Category", "Unit")]
public sealed class ProbeArgumentsTests
{
    /// <summary>The value after <c>-hwaccel</c> is returned, not <c>-hwaccel_output_format</c>'s.</summary>
    /// <param name="inputArgs">Generated input arguments.</param>
    /// <param name="expected">The hwaccel, or null.</param>
    [Theory]
    [InlineData("-init_hw_device qsv=qs@va -hwaccel vaapi -hwaccel_output_format vaapi -noautorotate", "vaapi")]
    [InlineData("-init_hw_device qsv=qs@va -c:v h264_qsv", null)]
    [InlineData("-hwaccel", null)]
    [InlineData("", null)]
    public void HwaccelIsReadFromInputArgs(string inputArgs, string? expected) =>
        Assert.Equal(expected, new ProbeArguments(inputArgs, string.Empty, "h264_qsv", new Dictionary<string, string?>()).Hwaccel);
}
