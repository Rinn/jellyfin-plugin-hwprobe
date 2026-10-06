using Jellyfin.Plugin.HwProbe.Core.Devices;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Devices;

/// <summary><see cref="HostInfoReader"/> on the real host.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class HostInfoReaderRealHostTests
{
    /// <summary>The real host reads without throwing.</summary>
    [Fact]
    public void RealHostDoesNotThrow() =>
        Assert.False(string.IsNullOrEmpty(new HostInfoReader(new HostPlatform()).Read().Kernel));
}
