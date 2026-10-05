using Jellyfin.Plugin.HwProbe.Core.Devices;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Devices;

/// <summary>Kernel and container detection in <see cref="HostInfoReader"/>.</summary>
[Trait("Category", "Unit")]
public sealed class HostInfoReaderTests
{
    /// <summary>Linux kernel comes from /proc/sys/kernel/osrelease, trimmed.</summary>
    [Fact]
    public void LinuxKernelFromProc()
    {
        var host = new FakeHostPlatform(HostOs.Linux);
        host.Files["/proc/sys/kernel/osrelease"] = "6.8.0-45-generic\n";

        Assert.Equal(new HostInfo(HostOs.Linux, "6.8.0-45-generic", null) { Architecture = "x64" }, new HostInfoReader(host).Read());
    }

    /// <summary>The CPU architecture comes from the platform.</summary>
    [Fact]
    public void ArchitectureFromPlatform() =>
        Assert.Equal("arm64", new HostInfoReader(new FakeHostPlatform(HostOs.Linux) { Architecture = "arm64" }).Read().Architecture);

    /// <summary>An unreadable osrelease yields unknown, not an exception.</summary>
    [Fact]
    public void LinuxKernelUnreadable() =>
        Assert.Equal("unknown", new HostInfoReader(new FakeHostPlatform(HostOs.Linux)).Read().Kernel);

    /// <summary>macOS kernel is the Darwin release, or the macOS version on .NET 10.</summary>
    /// <param name="description">The runtime's OS description.</param>
    /// <param name="expected">The expected kernel field.</param>
    [Theory]
    [InlineData("Darwin 25.0.0 Darwin Kernel Version 25.0.0: Mon Aug", "25.0.0")]
    [InlineData("macOS 27.0.1", "macOS 27.0.1")]
    [InlineData("macOS 26", "macOS 26")]
    [InlineData("Windows 11", "unknown")]
    public void MacOsKernelFromDarwinRelease(string description, string expected)
    {
        var host = new FakeHostPlatform(HostOs.MacOS) { OsDescription = description };

        Assert.Equal(expected, new HostInfoReader(host).Read().Kernel);
    }

    /// <summary>Windows reports the OS version and never a container.</summary>
    [Fact]
    public void WindowsUsesOsVersion()
    {
        var host = new FakeHostPlatform(HostOs.Windows) { OsVersion = new Version(10, 0, 22631, 0) };
        host.Files["/.dockerenv"] = string.Empty;

        Assert.Equal(new HostInfo(HostOs.Windows, "10.0.22631.0", null) { Architecture = "x64" }, new HostInfoReader(host).Read());
    }

    /// <summary>Container markers and cgroup contents map to runtime names.</summary>
    /// <param name="path">A file present on the fake host.</param>
    /// <param name="contents">Its contents.</param>
    /// <param name="expected">The detected runtime, or null.</param>
    [Theory]
    [InlineData("/.dockerenv", "", "docker")]
    [InlineData("/run/.containerenv", "", "podman")]
    [InlineData("/proc/1/cgroup", "0::/kubepods/besteffort/pod1/abc\n", "kubernetes")]
    [InlineData("/proc/1/cgroup", "12:pids:/docker/abc\n", "docker")]
    [InlineData("/proc/1/cgroup", "0::/system.slice/containerd.service\n", "containerd")]
    [InlineData("/proc/1/cgroup", "0::/lxc/web\n", "lxc")]
    [InlineData("/proc/1/cgroup", "0::/init.scope\n", null)]
    public void ContainerDetection(string path, string contents, string? expected)
    {
        var host = new FakeHostPlatform(HostOs.Linux);
        host.Files[path] = contents;

        Assert.Equal(expected, new HostInfoReader(host).Read().Container);
    }

    /// <summary>Synology DSM is detected from its kernel's syno_hw_version sysctl, on Linux only.</summary>
    /// <param name="os">The OS family.</param>
    /// <param name="expected">Whether Synology is reported.</param>
    [Theory]
    [InlineData(HostOs.Linux, true)]
    [InlineData(HostOs.Windows, false)]
    public void SynologyDetection(HostOs os, bool expected)
    {
        var host = new FakeHostPlatform(os);
        host.Files["/proc/sys/kernel/syno_hw_version"] = "DS1019+\n";

        Assert.Equal(expected, new HostInfoReader(host).Read().Synology);
    }

    /// <summary>The real host reads without throwing.</summary>
    [Fact]
    public void RealHostDoesNotThrow() =>
        Assert.False(string.IsNullOrEmpty(new HostInfoReader(new HostPlatform()).Read().Kernel));
}
