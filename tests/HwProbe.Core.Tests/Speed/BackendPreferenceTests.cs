using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>QSV in place of VAAPI in <see cref="BackendPreference"/>.</summary>
[Trait("Category", "Unit")]
public sealed class BackendPreferenceTests
{
    /// <summary>VAAPI becomes QSV only when QSV works on the same device; other backends stay.</summary>
    [Fact]
    public void PrefersQsvOnTheSameDevice()
    {
        const string Intel = "/dev/dri/renderD128";
        Assert.Equal((HwType.qsv, Intel), BackendPreference.Prefer((HwType.vaapi, Intel), [(HwType.vaapi, Intel), (HwType.qsv, Intel)]));
        Assert.Equal((HwType.vaapi, Intel), BackendPreference.Prefer((HwType.vaapi, Intel), [(HwType.vaapi, Intel)]));
        Assert.Equal((HwType.vaapi, "/dev/dri/renderD129"), BackendPreference.Prefer((HwType.vaapi, "/dev/dri/renderD129"), [(HwType.vaapi, "/dev/dri/renderD129"), (HwType.qsv, Intel)]));
        Assert.Equal((HwType.nvenc, string.Empty), BackendPreference.Prefer((HwType.nvenc, string.Empty), [(HwType.nvenc, string.Empty), (HwType.qsv, Intel)]));
    }
}
