using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Which variables EncodingHelper sets, per backend and driver.</summary>
[Trait("Category", "Unit")]
public sealed class EncodingHelperEnvironmentTests
{
    /// <summary>Only VAAPI on i965 or AMD writes anything; iHD wins over both.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="ihd">iHD driver.</param>
    /// <param name="i965">i965 driver.</param>
    /// <param name="amd">AMD driver.</param>
    /// <param name="expected">Expected writes, as <c>K=V,K=V</c>.</param>
    [Theory]
    [InlineData(HwType.vaapi, false, true, false, "LIBVA_DRIVER_NAME=i965,LIBVA_DRIVER_NAME_JELLYFIN=i965")]
    [InlineData(HwType.vaapi, false, false, true, "AMD_DEBUG=noefc")]
    [InlineData(HwType.vaapi, true, true, true, "")]
    [InlineData(HwType.vaapi, false, true, true, "LIBVA_DRIVER_NAME=i965,LIBVA_DRIVER_NAME_JELLYFIN=i965")]
    [InlineData(HwType.vaapi, false, false, false, "")]
    [InlineData(HwType.qsv, false, true, false, "")]
    [InlineData(HwType.nvenc, false, false, true, "")]
    public void PredictsWrites(HwType type, bool ihd, bool i965, bool amd, string expected) =>
        Assert.Equal(expected, string.Join(",", EncodingHelperEnvironment.Predict(type, ihd, i965, amd).Select(kv => $"{kv.Key}={kv.Value}")));
}
