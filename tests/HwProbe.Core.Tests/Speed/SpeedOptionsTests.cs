using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
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
        var empty = new Dictionary<string, ProbeOutcome>();
        var qsv = new BackendReport(HwType.qsv, "/dev/dri/renderD128", BackendVerdict.Viable, PipelineTier.FullOpencl, empty, encode, empty, empty, empty, string.Empty);
        var report = new CapabilityReport(CapabilityReport.CurrentSchemaVersion, DateTimeOffset.UnixEpoch, "f", new FfmpegSummary("/ffmpeg", "CommandLine", "8.1.2", true), new HostSummary("linux", "6.8", null), new StageASummary([], new Dictionary<HwType, BuildStatus>(), new Dictionary<string, bool>()), [qsv], [], []);

        Assert.Equal([(HwType.qsv, "/dev/dri/renderD128", "hevc")], SpeedOptions.MissingLowPower(report));
    }
}
