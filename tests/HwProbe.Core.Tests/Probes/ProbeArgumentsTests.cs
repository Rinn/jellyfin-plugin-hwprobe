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

    /// <summary>Encoder options follow the encoder name.</summary>
    [Fact]
    public void EncoderArgsFollowEncoder()
    {
        var args = new ProbeArguments("-hwaccel vaapi", string.Empty, "h264_vaapi", new Dictionary<string, string?>()) { EncoderArgs = " -low_power 1" };

        Assert.Contains("-c:v h264_vaapi -low_power 1 -an", ProbeCommandLine.Build(args, "/f.mp4", 10), StringComparison.Ordinal);
    }
}
