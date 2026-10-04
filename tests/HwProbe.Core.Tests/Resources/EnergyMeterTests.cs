using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Resources;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Resources;

/// <summary>Energy between readings in <see cref="EnergyMeter"/>, power above idle, and energy in the efficiency comparison.</summary>
[Trait("Category", "Unit")]
public sealed class EnergyMeterTests
{
    /// <summary>Readings are summed by domain, a counter that wrapped counts once around, and a source missing from either reading is left out.</summary>
    [Fact]
    public void SumsByDomainAndAllowsAWrap()
    {
        var package0 = new EnergySource("Cpu", 100, () => null);
        var package1 = new EnergySource("Cpu", null, () => null);
        var gpu = new EnergySource("Gpu", null, () => null);
        var arc = new EnergySource("Gpu", null, () => null);

        var used = EnergyMeter.Used(new() { [package0] = 90, [package1] = 10, [gpu] = 5 }, new() { [package0] = 20, [package1] = 15, [gpu] = 25, [arc] = 3 });

        Assert.Equal(new Dictionary<string, double> { ["Cpu"] = 35, ["Gpu"] = 20 }, used);
        Assert.Null(EnergyMeter.Used([], new() { [gpu] = 1 }));
    }

    /// <summary>Power above idle is the run's average less the idle reading, never below zero.</summary>
    [Fact]
    public void SubtractsIdle()
    {
        var usage = new ResourceUsage(10, null, null) { Joules = new Dictionary<string, double> { ["Gpu"] = 600, ["Cpu"] = 100 }, IdleWatts = new Dictionary<string, double> { ["Gpu"] = 20, ["Cpu"] = 15 } };

        Assert.Equal(new Dictionary<string, double> { ["Gpu"] = 40, ["Cpu"] = 0 }, usage.WattsAboveIdle());
    }

    /// <summary>A result that uses less energy per frame, measured alike otherwise, saves power.</summary>
    [Fact]
    public void ComparesEnergyPerFrame()
    {
        static SpeedResult Result(HwType type, double fps, double joules) =>
            new(type, string.Empty, "drama|h264-8mbps", string.Empty, fps, null, false, null) { Resources = new ResourceUsage(10, 5, 100_000_000) { Joules = new Dictionary<string, double> { ["Gpu"] = joules }, IdleWatts = new Dictionary<string, double> { ["Gpu"] = 10 } } };

        var savings = ResourceComparison.Savings(Result(HwType.nvenc, 500, 300), Result(HwType.qsv, 500, 500));

        Assert.Equal([("Power", 0.5)], savings.Select(s => (s.Resource, Math.Round(s.Fraction, 2))));
    }
}
