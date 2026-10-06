using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Reading a probe's low-power results in <see cref="SpeedOptions.MissingLowPower"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedOptionsTests
{
    /// <summary>A low-power encode test that didn't pass marks that backend and codec; one that passed, and ordinary encodes, don't.</summary>
    [Fact]
    public void MissingLowPowerFollowsTheProbe()
    {
        var encode = new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["h264_lowpower"] = ProbeOutcome.Pass, ["hevc"] = ProbeOutcome.Pass, ["hevc_lowpower"] = ProbeOutcome.CodecUnsupported };
        var report = Reports.With([Reports.Backend(HwType.qsv, "/dev/dri/renderD128", tier: PipelineTier.FullOpencl, encode: encode)]);

        Assert.Equal([(HwType.qsv, "/dev/dri/renderD128", "hevc")], SpeedOptions.MissingLowPower(report));
    }

    /// <summary>A decode the probe found failing marks that backend and test, so speed runs decode it in software; passes and codecs Jellyfin doesn't use there don't.</summary>
    [Fact]
    public void FailedDecodesFollowTheProbe()
    {
        var decode = new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["av1_10bit"] = ProbeOutcome.CodecUnsupported, ["mpeg4"] = ProbeOutcome.NotUsed };
        var report = Reports.With([Reports.Backend(HwType.qsv, "/dev/dri/renderD128", tier: PipelineTier.FullOpencl, decode: decode)]);

        Assert.Equal([(HwType.qsv, "/dev/dri/renderD128", "av1_10bit")], new SpeedOptions(SpeedMethod.Quick, [], [], new SpeedSettings()).ForReport(report).DecodeUnsupported);
    }
}
