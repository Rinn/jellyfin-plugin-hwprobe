using Jellyfin.Plugin.HwProbe.Core.Resources;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Resources;

/// <summary>Reading Windows "GPU Engine" instance names in <see cref="GpuEngineCounters"/>.</summary>
[Trait("Category", "Unit")]
public sealed class GpuEngineCountersTests
{
    /// <summary>The process id and engine type come from either end of the name.</summary>
    /// <param name="instance">The instance name.</param>
    /// <param name="pid">The expected process, or -1 for a name that doesn't parse.</param>
    /// <param name="engine">The expected engine type.</param>
    [Theory]
    [InlineData("pid_1234_luid_0x00000000_0x0000C2F3_phys_0_eng_3_engtype_VideoDecode", 1234, "VideoDecode")]
    [InlineData("pid_8_luid_0x00000000_0x0000C2F3_phys_0_eng_0_engtype_3D", 8, "3D")]
    [InlineData("_Total", -1, "")]
    [InlineData("pid_x_luid_0_engtype_3D", -1, "")]
    public void ParsesInstanceNames(string instance, int pid, string engine)
    {
        var parsed = GpuEngineCounters.Parse(instance);

        Assert.Equal(pid < 0 ? null : (pid, engine), parsed);
    }
}
