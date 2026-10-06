using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Devices;

/// <summary><see cref="DeviceEnumerator"/> on the real host.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class DeviceEnumeratorRealHostTests
{
    /// <summary>The real host enumerates without throwing.</summary>
    [Fact]
    public void RealHostDoesNotThrow() => new DeviceEnumerator(new HostPlatform()).Enumerate();

    /// <summary>A real macOS host enumerates VideoToolbox alone.</summary>
    [Fact(Skip = "Requires macOS.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]
    public void RealMacOSHostIsVideoToolboxOnly() =>
        Assert.Equal([new DeviceCandidate(HwType.videotoolbox, string.Empty)], new DeviceEnumerator(new HostPlatform()).Enumerate().Candidates);
}
