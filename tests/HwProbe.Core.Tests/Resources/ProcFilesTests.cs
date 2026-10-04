using Jellyfin.Plugin.HwProbe.Core.Resources;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Resources;

/// <summary>Reading Linux's per-process files in <see cref="ProcFiles"/>.</summary>
[Trait("Category", "Unit")]
public sealed class ProcFilesTests
{
    /// <summary>CPU ticks are fields 14 and 15, counted after the command name, which can hold spaces and parentheses.</summary>
    [Fact]
    public void CpuTicksSkipTheCommandName()
    {
        const string Stat = "4242 (ffmpeg (x) y) R 1 4242 4242 0 -1 4194304 1234 0 0 0 350 125 0 0 20 0 9 0 100 1000000 500 18446744073709551615";

        Assert.Equal(475, ProcFiles.CpuTicks(Stat));
        Assert.Null(ProcFiles.CpuTicks("garbled"));
    }

    /// <summary>Peak memory is VmHWM, in kibibytes.</summary>
    [Fact]
    public void PeakIsVmHwm()
    {
        Assert.Equal(123_456L * 1024, ProcFiles.PeakBytes("Name:\tffmpeg\nVmPeak:\t  999999 kB\nVmHWM:\t  123456 kB\nVmRSS:\t   1000 kB\n"));
        Assert.Null(ProcFiles.PeakBytes("Name:\tffmpeg\n"));
    }

    /// <summary>i915 and amdgpu report busy nanoseconds per engine; capacities and memory lines aren't engines.</summary>
    [Fact]
    public void DrmEnginesInNanoseconds()
    {
        const string Fdinfo = "pos:\t0\nflags:\t02100002\ndrm-driver:\ti915\ndrm-pdev:\t0000:00:02.0\ndrm-client-id:\t7\ndrm-engine-render:\t1000 ns\ndrm-engine-video:\t250000000 ns\ndrm-engine-capacity-video:\t2\ndrm-total-system0:\t4 MiB\n";

        var client = ProcFiles.Drm(Fdinfo)!;

        Assert.Equal("0000:00:02.0/7", client.Id);
        Assert.Equal(new Dictionary<string, long> { ["render"] = 1000, ["video"] = 250_000_000 }, client.Nanoseconds);
    }

    /// <summary>xe reports busy and total cycles per engine instead.</summary>
    [Fact]
    public void DrmEnginesInCycles()
    {
        var client = ProcFiles.Drm("drm-driver:\txe\ndrm-client-id:\t3\ndrm-cycles-vcs:\t500\ndrm-total-cycles-vcs:\t2000\n")!;

        Assert.Equal((500L, 2000L), client.Cycles["vcs"]);
    }

    /// <summary>A descriptor that isn't a DRM client has no client id.</summary>
    [Fact]
    public void OtherDescriptorsAreSkipped() => Assert.Null(ProcFiles.Drm("pos:\t0\nflags:\t02\nmnt_id:\t25\n"));
}
