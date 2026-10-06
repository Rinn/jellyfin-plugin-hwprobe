using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Devices;

/// <summary>Per-OS candidate selection and render-node facts from <see cref="DeviceEnumerator"/>.</summary>
[Trait("Category", "Unit")]
public sealed class DeviceEnumeratorTests
{
    /// <summary>Every render node is a vaapi and qsv candidate, in numeric order.</summary>
    [Fact]
    public void LinuxRenderNodesFeedVaapiAndQsvInNumericOrder()
    {
        var host = new FakeHostPlatform(HostOs.Linux);
        host.Files["/dev/dri/renderD1000"] = string.Empty;
        host.Files["/dev/dri/renderD129"] = string.Empty;
        host.Files["/dev/dri/renderD128"] = string.Empty;
        host.Files["/dev/dri/card0"] = string.Empty;

        var result = new DeviceEnumerator(host).Enumerate();

        string[] expected = ["/dev/dri/renderD128", "/dev/dri/renderD129", "/dev/dri/renderD1000"];
        Assert.Equal(expected, DevicesOf(result, HwType.vaapi));
        Assert.Equal(expected, DevicesOf(result, HwType.qsv));
        Assert.Equal(DirectoryAccess.Ok, result.RenderNodeAccess);
    }

    /// <summary>Linux also yields CUDA indices 0..3 and device-less rkmpp and v4l2m2m.</summary>
    [Fact]
    public void LinuxNonRenderBackends()
    {
        var result = new DeviceEnumerator(new FakeHostPlatform(HostOs.Linux)).Enumerate();

        Assert.Equal(["0", "1", "2", "3"], DevicesOf(result, HwType.nvenc));
        Assert.Equal([string.Empty], DevicesOf(result, HwType.rkmpp));
        Assert.Equal([string.Empty], DevicesOf(result, HwType.v4l2m2m));
        Assert.Empty(DevicesOf(result, HwType.amf));
        Assert.Empty(DevicesOf(result, HwType.videotoolbox));
        Assert.Empty(DevicesOf(result, HwType.none));
    }

    /// <summary>No /dev/dri means no render-node candidates, reported as Missing.</summary>
    [Fact]
    public void LinuxWithoutDri()
    {
        var result = new DeviceEnumerator(new FakeHostPlatform(HostOs.Linux)).Enumerate();

        Assert.Empty(DevicesOf(result, HwType.vaapi));
        Assert.Equal(DirectoryAccess.Missing, result.RenderNodeAccess);
        Assert.Empty(result.RenderNodes);
    }

    /// <summary>An unreadable /dev/dri is reported as Denied, not thrown.</summary>
    [Fact]
    public void LinuxUnreadableDri()
    {
        var host = new FakeHostPlatform(HostOs.Linux);
        host.DeniedDirectories.Add("/dev/dri");

        var result = new DeviceEnumerator(host).Enumerate();

        Assert.Equal(DirectoryAccess.Denied, result.RenderNodeAccess);
        Assert.Empty(DevicesOf(result, HwType.vaapi));
    }

    /// <summary>sysfs vendor/device are trimmed; missing, unreadable or blank fields become unknown.</summary>
    [Fact]
    public void RenderNodeIdentityFromSysfs()
    {
        var host = new FakeHostPlatform(HostOs.Linux);
        host.Files["/dev/dri/renderD128"] = string.Empty;
        host.Files["/dev/dri/renderD129"] = string.Empty;
        host.Files["/sys/class/drm/renderD128/device/vendor"] = "0x8086\n";
        host.Files["/sys/class/drm/renderD128/device/device"] = "0x46a6\n";
        host.Files["/sys/class/drm/renderD129/device/vendor"] = null;
        host.Files["/sys/class/drm/renderD129/device/device"] = "  \n";

        var result = new DeviceEnumerator(host).Enumerate();

        Assert.Equal(
            [
                new RenderNodeIdentity("/dev/dri/renderD128", "0x8086", "0x46a6"),
                new RenderNodeIdentity("/dev/dri/renderD129", "unknown", "unknown"),
            ],
            result.RenderNodes);
    }

    /// <summary>Windows yields adapter indices for qsv, amf and nvenc, and no render-node facts.</summary>
    [Fact]
    public void WindowsAdapterIndices()
    {
        var result = new DeviceEnumerator(new FakeHostPlatform(HostOs.Windows)).Enumerate();

        Assert.Equal(["0", "1", "2", "3"], DevicesOf(result, HwType.qsv));
        Assert.Equal(["0", "1", "2", "3"], DevicesOf(result, HwType.amf));
        Assert.Equal(["0", "1", "2", "3"], DevicesOf(result, HwType.nvenc));
        Assert.Empty(DevicesOf(result, HwType.vaapi));
        Assert.Empty(DevicesOf(result, HwType.videotoolbox));
        Assert.Null(result.RenderNodeAccess);
    }

    /// <summary>macOS yields only a device-less VideoToolbox candidate.</summary>
    [Fact]
    public void MacOsVideoToolboxOnly()
    {
        var result = new DeviceEnumerator(new FakeHostPlatform(HostOs.MacOS)).Enumerate();

        Assert.Equal([new DeviceCandidate(HwType.videotoolbox, string.Empty)], result.Candidates);
        Assert.Null(result.RenderNodeAccess);
        Assert.Empty(result.RenderNodes);
    }

    /// <summary>Returns the device selectors enumerated for one backend.</summary>
    /// <param name="result">The enumeration.</param>
    /// <param name="type">The backend.</param>
    /// <returns>Its device selectors, in order.</returns>
    private static List<string> DevicesOf(DeviceEnumeration result, HwType type) =>
        [.. result.Candidates.Where(c => c.Type == type).Select(c => c.Device)];
}
