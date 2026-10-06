using Jellyfin.Plugin.HwProbe.Core.Data;
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
    /// <param name="key">The catalog text expected.</param>
    [Theory]
    [InlineData(ProbeOutcome.PermissionDenied, HwType.vaapi, HostOs.Linux, false, "permissionDeniedHost")]
    [InlineData(ProbeOutcome.PermissionDenied, HwType.qsv, HostOs.Linux, true, "permissionDeniedContainer")]
    [InlineData(ProbeOutcome.PermissionDenied, HwType.nvenc, HostOs.Linux, false, "permissionDenied")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.vaapi, HostOs.Linux, true, "deviceUnavailableNodeContainer")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.qsv, HostOs.Linux, false, "deviceUnavailableNode")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.qsv, HostOs.Windows, false, "deviceUnavailableQsvWindows")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.amf, HostOs.Windows, false, "deviceUnavailableAmf")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.nvenc, HostOs.Linux, false, "deviceUnavailableNvenc")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.nvenc, HostOs.Linux, true, "deviceUnavailableNvencContainer")]
    [InlineData(ProbeOutcome.DeviceUnavailable, HwType.rkmpp, HostOs.Linux, false, "deviceUnavailable")]
    public void PicksEnvironmentSpecificRemedy(ProbeOutcome outcome, HwType type, HostOs os, bool inContainer, string key) =>
        Assert.Equal(Catalog.Text(key), Hints.For(outcome, type, os, inContainer));

    /// <summary>Container remedies name the flag that passes the device in.</summary>
    [Fact]
    public void ContainerRemediesNameTheFlag()
    {
        Assert.Contains("--device /dev/dri/renderD128", Hints.For(ProbeOutcome.DeviceUnavailable, HwType.vaapi, HostOs.Linux, inContainer: true), StringComparison.Ordinal);
        Assert.Contains("--group-add", Hints.For(ProbeOutcome.PermissionDenied, HwType.qsv, HostOs.Linux, inContainer: true), StringComparison.Ordinal);
        Assert.Contains("--gpus all", Hints.For(ProbeOutcome.DeviceUnavailable, HwType.nvenc, HostOs.Linux, inContainer: true), StringComparison.Ordinal);
    }

    /// <summary>An out-of-range outcome is rejected.</summary>
    [Fact]
    public void UnknownOutcomeThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Hints.For((ProbeOutcome)99, HwType.vaapi, HostOs.Linux, false));

    /// <summary>Backends the user can fix get a short action and a link into Jellyfin's guides; missing hardware and missing builds get none.</summary>
    /// <param name="verdict">The backend's verdict.</param>
    /// <param name="type">The backend.</param>
    /// <param name="inContainer">Whether the probe ran in a container.</param>
    /// <param name="actionKey">The catalog text of the expected action, or null for no fix.</param>
    /// <param name="guide">The expected link's path under the guides, or null.</param>
    [Theory]
    [InlineData(BackendVerdict.NotPresent, HwType.nvenc, true, "fixGpusAll", "nvidia/#official-docker")]
    [InlineData(BackendVerdict.NotPresent, HwType.qsv, true, "fixDeviceDri", "intel/#official-docker")]
    [InlineData(BackendVerdict.NotPresent, HwType.vaapi, true, "fixDeviceDri", "")]
    [InlineData(BackendVerdict.PermissionDenied, HwType.qsv, false, "fixRenderGroupHost", "intel/#configure-on-linux-host")]
    [InlineData(BackendVerdict.NotPresent, HwType.nvenc, false, null, null)]
    [InlineData(BackendVerdict.NotPresent, HwType.v4l2m2m, true, null, null)]
    [InlineData(BackendVerdict.DevicePresentPipelineBroken, HwType.qsv, true, null, null)]
    public void FixForBackend(BackendVerdict verdict, HwType type, bool inContainer, string? actionKey, string? guide)
    {
        var fix = Hints.FixFor(verdict, type, HostOs.Linux, inContainer);

        Assert.Equal(actionKey is null ? null : Catalog.Text(actionKey), fix?.Action);
        Assert.Equal(guide is null ? null : Catalog.Default.Links["jellyfinGuides"] + guide, fix?.Url?.ToString());
    }
}
