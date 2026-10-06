using Jellyfin.Plugin.HwProbe.Core.Data;
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
        Assert.Equal(Catalog.Text("lowPowerHevcMaybeGen9", ("remedy", LowPowerAdvice.Remedy(Linux(inContainer: true, "0")))), LowPowerAdvice.Remedy("hevc", Linux(inContainer: true, "0"), LowPowerSupport.Unknown));
        Assert.Equal(LowPowerAdvice.Remedy(Linux(inContainer: true, "0")), LowPowerAdvice.Remedy("h264", Linux(inContainer: true, "0"), LowPowerSupport.Unknown));
    }

    /// <summary>On a known Gen 9 GPU failed low-power HEVC is expected, and on Gen 8 or older both are; neither gets a firmware remedy.</summary>
    [Fact]
    public void OlderGenerationsHaveNoRemedy()
    {
        var gen9Hevc = LowPowerAdvice.Remedy("hevc", Linux(inContainer: true, "0"), LowPowerSupport.H264Only);
        var gen8H264 = LowPowerAdvice.Remedy("h264", Linux(inContainer: true, "0"), LowPowerSupport.None);

        Assert.Equal(Catalog.Text("lowPowerHevcGen9"), gen9Hevc);
        Assert.Equal(Catalog.Text("lowPowerNone"), gen8H264);
        Assert.DoesNotContain("enable_guc", gen9Hevc + gen8H264, StringComparison.Ordinal);
        Assert.Equal(LowPowerAdvice.Remedy(Linux(inContainer: true, "0")), LowPowerAdvice.Remedy("h264", Linux(inContainer: true, "0"), LowPowerSupport.H264Only));
    }

    /// <summary>On Linux with i915, the remedy names the firmware, enable_guc=2 and its current value, and links the guide.</summary>
    [Fact]
    public void LinuxRemedyNamesFirmwareAndGuc()
    {
        var remedy = LowPowerAdvice.Remedy(Linux(inContainer: false, "-1"));

        Assert.Equal(Catalog.Text("lowPowerFirmware", ("where", Catalog.Text("lowPowerFirmwareHost")), ("current", Catalog.Text("lowPowerEnableGuc", ("value", "-1"))), ("guide", LowPowerAdvice.Guide.ToString())), remedy);
        Assert.Contains("enable_guc=2", remedy, StringComparison.Ordinal);
        Assert.Contains("enable_guc is currently -1", remedy, StringComparison.Ordinal);
        Assert.Contains(LowPowerAdvice.Guide.ToString(), remedy, StringComparison.Ordinal);
    }

    /// <summary>In a container the remedy points at the host; without i915 and off Linux it doesn't mention enable_guc.</summary>
    [Fact]
    public void RemedyAdaptsToEnvironment()
    {
        var noI915 = LowPowerAdvice.Remedy(new LowPowerHost(HostOs.Linux, InContainer: false, I915Loaded: false, EnableGuc: null));
        var windows = LowPowerAdvice.Remedy(new LowPowerHost(HostOs.Windows, InContainer: false, I915Loaded: false, EnableGuc: null));

        Assert.Equal(Catalog.Text("lowPowerFirmware", ("where", Catalog.Text("lowPowerFirmwareContainer")), ("current", Catalog.Text("lowPowerEnableGuc", ("value", "2"))), ("guide", LowPowerAdvice.Guide.ToString())), LowPowerAdvice.Remedy(Linux(inContainer: true, "2")));
        Assert.Equal(Catalog.Text("lowPowerNoI915", ("guide", LowPowerAdvice.Guide.ToString())), noI915);
        Assert.Equal(Catalog.Text("lowPowerNotLinux"), windows);
        Assert.DoesNotContain("enable_guc=2", noI915, StringComparison.Ordinal);
        Assert.DoesNotContain("enable_guc", windows, StringComparison.Ordinal);
    }

    /// <summary>A loaded i915 whose enable_guc only root can read still gets the firmware remedy, not "i915 isn't loaded".</summary>
    [Fact]
    public void RootOnlyEnableGucKeepsFirmwareRemedy()
    {
        var remedy = LowPowerAdvice.Remedy(Linux(inContainer: true, null));

        Assert.Contains("enable_guc=2", remedy, StringComparison.Ordinal);
        Assert.Contains(Catalog.Text("lowPowerEnableGucRootOnly", ("path", LowPowerAdvice.EnableGucPath)), remedy, StringComparison.Ordinal);
        Assert.NotEqual(Catalog.Text("lowPowerNoI915", ("guide", LowPowerAdvice.Guide.ToString())), remedy);
    }

    /// <summary>enable_guc's HuC bit decides when readable; -1 means off on Gen 9 and older and is unknown on newer GPUs, where the kernel's default may load HuC.</summary>
    /// <param name="enableGuc">The enable_guc value, or null when unreadable.</param>
    /// <param name="support">The GPU generation's low-power encoders.</param>
    /// <param name="expected">Whether HuC is requested, or null when unknown.</param>
    [Theory]
    [InlineData("2", LowPowerSupport.H264Only, true)]
    [InlineData("3", LowPowerSupport.Unknown, true)]
    [InlineData("1", LowPowerSupport.Unknown, false)]
    [InlineData("0", LowPowerSupport.Unknown, false)]
    [InlineData("-1", LowPowerSupport.H264Only, false)]
    [InlineData("-1", LowPowerSupport.None, false)]
    [InlineData("-1", LowPowerSupport.Unknown, null)]
    [InlineData(null, LowPowerSupport.H264Only, null)]
    public void HucRequestedFollowsEnableGuc(string? enableGuc, LowPowerSupport support, bool? expected) =>
        Assert.Equal(expected, Linux(inContainer: false, enableGuc).HucRequested(support));

    /// <summary>Builds Linux host facts with i915 loaded.</summary>
    /// <param name="inContainer">Whether the probe ran in a container.</param>
    /// <param name="enableGuc">The enable_guc value, or null when unreadable.</param>
    /// <returns>The host facts.</returns>
    private static LowPowerHost Linux(bool inContainer, string? enableGuc) => new(HostOs.Linux, inContainer, I915Loaded: true, enableGuc);
}
