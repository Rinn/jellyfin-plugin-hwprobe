using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;

/// <summary>Advice for the Intel Low-Power encoder options.</summary>
[Trait("Category", "Unit")]
public sealed class LowPowerAdviceTests
{
    /// <summary>The finding follows from which of the normal and low-power encodes work.</summary>
    /// <param name="normal">Normal encode outcome.</param>
    /// <param name="lowPower">Low-power encode outcome.</param>
    /// <param name="expectedCode">The finding code, or empty for none.</param>
    [Theory]
    [InlineData(ProbeOutcome.CodecUnsupported, ProbeOutcome.Pass, "enable-lowpower-h264")]
    [InlineData(ProbeOutcome.Pass, ProbeOutcome.Pass, "lowpower-available-h264")]
    [InlineData(ProbeOutcome.Pass, ProbeOutcome.CodecUnsupported, "lowpower-unavailable-h264")]
    [InlineData(ProbeOutcome.CodecUnsupported, ProbeOutcome.CodecUnsupported, "")]
    [InlineData(ProbeOutcome.Pass, ProbeOutcome.Skipped, "")]
    public void FindingMatchesResults(ProbeOutcome normal, ProbeOutcome lowPower, string expectedCode)
    {
        var encode = new Dictionary<string, ProbeOutcome> { ["h264"] = normal, ["h264_lowpower"] = lowPower };

        var codes = LowPowerAdvice.Findings(HwType.qsv, "/dev/dri/renderD128", encode, HostOs.Linux, inContainer: false, "-1", LowPowerSupport.Unknown).Select(f => f.Code);

        Assert.Equal(string.IsNullOrEmpty(expectedCode) ? [] : [expectedCode], codes);
    }

    /// <summary>Failed low-power HEVC says Gen 9 has low-power H.264 only; H.264 gets the firmware remedy alone.</summary>
    [Fact]
    public void HevcRemedyNamesGen9Limit()
    {
        Assert.StartsWith("Gen 9 Intel graphics", LowPowerAdvice.Remedy("hevc", HostOs.Linux, inContainer: true, "0", LowPowerSupport.Unknown), StringComparison.Ordinal);
        Assert.Equal(LowPowerAdvice.Remedy(HostOs.Linux, inContainer: true, "0"), LowPowerAdvice.Remedy("h264", HostOs.Linux, inContainer: true, "0", LowPowerSupport.Unknown));
    }

    /// <summary>On a known Gen 9 GPU failed low-power HEVC is expected, and on Gen 8 or older both are; neither gets a firmware remedy.</summary>
    [Fact]
    public void OlderGenerationsHaveNoRemedy()
    {
        var gen9Hevc = LowPowerAdvice.Remedy("hevc", HostOs.Linux, inContainer: true, "0", LowPowerSupport.H264Only);
        var gen8H264 = LowPowerAdvice.Remedy("h264", HostOs.Linux, inContainer: true, "0", LowPowerSupport.None);

        Assert.StartsWith("This GPU is Gen 9 Intel graphics", gen9Hevc, StringComparison.Ordinal);
        Assert.StartsWith("This GPU is Gen 8 Intel graphics or older", gen8H264, StringComparison.Ordinal);
        Assert.DoesNotContain("enable_guc", gen9Hevc + gen8H264, StringComparison.Ordinal);
        Assert.Equal(LowPowerAdvice.Remedy(HostOs.Linux, inContainer: true, "0"), LowPowerAdvice.Remedy("h264", HostOs.Linux, inContainer: true, "0", LowPowerSupport.H264Only));
    }

    /// <summary>On Linux with i915, the remedy names the firmware, enable_guc=2 and its current value, and links the guide.</summary>
    [Fact]
    public void LinuxRemedyNamesFirmwareAndGuc()
    {
        var remedy = LowPowerAdvice.Remedy(HostOs.Linux, inContainer: false, "-1");

        Assert.Contains("HuC firmware", remedy, StringComparison.Ordinal);
        Assert.Contains("enable_guc=2", remedy, StringComparison.Ordinal);
        Assert.Contains("enable_guc is currently -1", remedy, StringComparison.Ordinal);
        Assert.Contains(LowPowerAdvice.GuideUrl, remedy, StringComparison.Ordinal);
    }

    /// <summary>In a container the remedy points at the host; without i915 and off Linux it doesn't mention enable_guc.</summary>
    [Fact]
    public void RemedyAdaptsToEnvironment()
    {
        Assert.Contains("On the host", LowPowerAdvice.Remedy(HostOs.Linux, inContainer: true, "2"), StringComparison.Ordinal);
        Assert.DoesNotContain("enable_guc=2", LowPowerAdvice.Remedy(HostOs.Linux, inContainer: false, null), StringComparison.Ordinal);
        Assert.DoesNotContain("enable_guc", LowPowerAdvice.Remedy(HostOs.Windows, inContainer: false, null), StringComparison.Ordinal);
    }
}
