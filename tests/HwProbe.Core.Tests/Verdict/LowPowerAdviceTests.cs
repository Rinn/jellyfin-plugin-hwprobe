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

        var codes = LowPowerAdvice.Findings(HwType.qsv, "/dev/dri/renderD128", encode, Linux(inContainer: false, "-1"), LowPowerSupport.Unknown).Select(f => f.Code);

        Assert.Equal(string.IsNullOrEmpty(expectedCode) ? [] : [expectedCode], codes);
    }

    /// <summary>Failed low-power HEVC says Gen 9 has low-power H.264 only; H.264 gets the firmware remedy alone.</summary>
    [Fact]
    public void HevcRemedyNamesGen9Limit()
    {
        Assert.StartsWith("Gen 9 Intel graphics", LowPowerAdvice.Remedy("hevc", Linux(inContainer: true, "0"), LowPowerSupport.Unknown), StringComparison.Ordinal);
        Assert.Equal(LowPowerAdvice.Remedy(Linux(inContainer: true, "0")), LowPowerAdvice.Remedy("h264", Linux(inContainer: true, "0"), LowPowerSupport.Unknown));
    }

    /// <summary>On a known Gen 9 GPU failed low-power HEVC is expected, and on Gen 8 or older both are; neither gets a firmware remedy.</summary>
    [Fact]
    public void OlderGenerationsHaveNoRemedy()
    {
        var gen9Hevc = LowPowerAdvice.Remedy("hevc", Linux(inContainer: true, "0"), LowPowerSupport.H264Only);
        var gen8H264 = LowPowerAdvice.Remedy("h264", Linux(inContainer: true, "0"), LowPowerSupport.None);

        Assert.StartsWith("This GPU is Gen 9 Intel graphics", gen9Hevc, StringComparison.Ordinal);
        Assert.StartsWith("This GPU is Gen 8 Intel graphics or older", gen8H264, StringComparison.Ordinal);
        Assert.DoesNotContain("enable_guc", gen9Hevc + gen8H264, StringComparison.Ordinal);
        Assert.Equal(LowPowerAdvice.Remedy(Linux(inContainer: true, "0")), LowPowerAdvice.Remedy("h264", Linux(inContainer: true, "0"), LowPowerSupport.H264Only));
    }

    /// <summary>On Linux with i915, the remedy names the firmware, enable_guc=2 and its current value, and links the guide.</summary>
    [Fact]
    public void LinuxRemedyNamesFirmwareAndGuc()
    {
        var remedy = LowPowerAdvice.Remedy(Linux(inContainer: false, "-1"));

        Assert.Contains("HuC firmware", remedy, StringComparison.Ordinal);
        Assert.Contains("enable_guc=2", remedy, StringComparison.Ordinal);
        Assert.Contains("enable_guc is currently -1", remedy, StringComparison.Ordinal);
        Assert.Contains(LowPowerAdvice.Guide.ToString(), remedy, StringComparison.Ordinal);
    }

    /// <summary>In a container the remedy points at the host; without i915 and off Linux it doesn't mention enable_guc.</summary>
    [Fact]
    public void RemedyAdaptsToEnvironment()
    {
        Assert.Contains("On the host", LowPowerAdvice.Remedy(Linux(inContainer: true, "2")), StringComparison.Ordinal);
        Assert.DoesNotContain("enable_guc=2", LowPowerAdvice.Remedy(new LowPowerHost(HostOs.Linux, InContainer: false, Synology: false, I915Loaded: false, EnableGuc: null)), StringComparison.Ordinal);
        Assert.DoesNotContain("enable_guc", LowPowerAdvice.Remedy(new LowPowerHost(HostOs.Windows, InContainer: false, Synology: false, I915Loaded: false, EnableGuc: null)), StringComparison.Ordinal);
    }

    /// <summary>A loaded i915 whose enable_guc only root can read still gets the firmware remedy, not "i915 isn't loaded".</summary>
    [Fact]
    public void RootOnlyEnableGucKeepsFirmwareRemedy()
    {
        var remedy = LowPowerAdvice.Remedy(Linux(inContainer: true, null));

        Assert.Contains("enable_guc=2", remedy, StringComparison.Ordinal);
        Assert.Contains("Only root can read enable_guc", remedy, StringComparison.Ordinal);
        Assert.DoesNotContain("isn't loaded", remedy, StringComparison.Ordinal);
    }

    /// <summary>On Synology DSM the remedy copies the firmware files and creates /etc/modprobe.d instead of installing a package and updating the initramfs.</summary>
    [Fact]
    public void SynologyRemedyCopiesFirmware()
    {
        var remedy = LowPowerAdvice.Remedy(new LowPowerHost(HostOs.Linux, InContainer: true, Synology: true, I915Loaded: true, EnableGuc: null));

        Assert.StartsWith("Low-power encoding on Linux requires Intel's HuC firmware. On the NAS (not in the container), as root,", remedy, StringComparison.Ordinal);
        Assert.Contains(LowPowerAdvice.FirmwareFiles.ToString(), remedy, StringComparison.Ordinal);
        Assert.Contains("mkdir -p /etc/modprobe.d", remedy, StringComparison.Ordinal);
        Assert.DoesNotContain("initramfs", remedy, StringComparison.Ordinal);
    }

    /// <summary>Builds Linux host facts with i915 loaded.</summary>
    /// <param name="inContainer">Whether the probe ran in a container.</param>
    /// <param name="enableGuc">The enable_guc value, or null when unreadable.</param>
    /// <returns>The host facts.</returns>
    private static LowPowerHost Linux(bool inContainer, string? enableGuc) => new(HostOs.Linux, inContainer, Synology: false, I915Loaded: true, enableGuc);
}
