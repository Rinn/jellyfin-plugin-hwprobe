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

    /// <summary>Backends the user can fix get a short action and a link; missing hardware and missing builds get none.</summary>
    /// <param name="verdict">The backend's verdict.</param>
    /// <param name="type">The backend.</param>
    /// <param name="inContainer">Whether the probe ran in a container.</param>
    /// <param name="action">The expected action, or null for no fix.</param>
    /// <param name="link">The expected link.</param>
    [Theory]
    [InlineData(BackendVerdict.NotPresent, HwType.nvenc, true, "Pass the GPU to the container (--gpus all)", "https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/nvidia/#official-docker")]
    [InlineData(BackendVerdict.NotPresent, HwType.qsv, true, "Pass the GPU to the container (--device /dev/dri)", "https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/intel/#official-docker")]
    [InlineData(BackendVerdict.NotPresent, HwType.vaapi, true, "Pass the GPU to the container (--device /dev/dri)", "https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/")]
    [InlineData(BackendVerdict.PermissionDenied, HwType.qsv, false, "Add jellyfin to the render group (usermod)", "https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/intel/#configure-on-linux-host")]
    [InlineData(BackendVerdict.NotPresent, HwType.nvenc, false, null, null)]
    [InlineData(BackendVerdict.NotPresent, HwType.v4l2m2m, true, null, null)]
    [InlineData(BackendVerdict.DevicePresentPipelineBroken, HwType.qsv, true, null, null)]
    public void FixForBackend(BackendVerdict verdict, HwType type, bool inContainer, string? action, string? link)
    {
        var fix = Hints.FixFor(verdict, type, HostOs.Linux, inContainer);

        Assert.Equal(action, fix?.Action);
        Assert.Equal(link, fix?.Url?.ToString());
    }
}
