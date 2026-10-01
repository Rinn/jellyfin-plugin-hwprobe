using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;

/// <summary>Remedy text selection.</summary>
[Trait("Category", "Unit")]
public sealed class HintsTests
{
    /// <summary>Every outcome and backend has a hint; only a pass has an empty one.</summary>
    [Fact]
    public void EveryCombinationHasHint()
    {
        foreach (var outcome in Enum.GetValues<ProbeOutcome>())
        {
            foreach (var type in Enum.GetValues<HwType>())
            {
                foreach (var inContainer in (bool[])[false, true])
                {
                    var hint = Hints.For(outcome, type, HostOs.Linux, inContainer);
                    Assert.Equal(outcome == ProbeOutcome.Pass, hint.Length == 0);
                }
            }
        }
    }

    /// <summary>Hints that differ by environment pick the right remedy.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="type">The backend.</param>
    /// <param name="os">The host OS.</param>
    /// <param name="inContainer">Whether in a container.</param>
    /// <param name="expectedText">Text the hint must contain.</param>
    [Theory]
    [InlineData(ProbeOutcome.PermissionDenied, HwType.vaapi, HostOs.Linux, false, "render group")]
    [InlineData(ProbeOutcome.PermissionDenied, HwType.qsv, HostOs.Linux, true, "--group-add")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.vaapi, HostOs.Linux, true, "--device /dev/dri/renderD128")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.qsv, HostOs.Linux, false, "ls /dev/dri")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.qsv, HostOs.Windows, false, "Direct3D 11")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.amf, HostOs.Windows, false, "AMD graphics driver")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.nvenc, HostOs.Linux, false, "no NVIDIA device")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.nvenc, HostOs.Linux, true, "--gpus all")]
    public void PicksEnvironmentSpecificRemedy(ProbeOutcome outcome, HwType type, HostOs os, bool inContainer, string expectedText) =>
        Assert.Contains(expectedText, Hints.For(outcome, type, os, inContainer), StringComparison.Ordinal);

    /// <summary>An out-of-range outcome is rejected.</summary>
    [Fact]
    public void UnknownOutcomeThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Hints.For((ProbeOutcome)99, HwType.vaapi, HostOs.Linux, false));
}
