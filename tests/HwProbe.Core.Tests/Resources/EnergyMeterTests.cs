using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Resources;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Resources;

/// <summary>Energy between readings in <see cref="EnergyMeter"/> and <see cref="EnergySpan"/>, power above idle, and energy in the efficiency comparison.</summary>
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

    /// <summary>Power samples are joined by straight lines, so a ramp counts its average, and one sample spans no time.</summary>
    [Fact]
    public void IntegratesPowerSamples()
    {
        var integral = new PowerIntegral();
        integral.Add(5, 10);
        Assert.False(integral.Spans);

        integral.Add(7, 30);
        integral.Add(8, 30);

        Assert.True(integral.Spans);
        Assert.Equal(70, integral.Joules, 9);
    }

    /// <summary>A span adds a power meter's energy to a counter's in the same domain.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SpanAddsPowerMetersToCounters()
    {
        var counted = 0.0;
        var counter = new EnergySource("Gpu", null, () => counted);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await using var span = new EnergySpan([counter], [new PowerSource("Gpu", () => 10)]);
        counted = 2;
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        var used = await span.StopAsync();
        var elapsed = clock.Elapsed.TotalSeconds;

        Assert.NotNull(used);
        Assert.InRange(used["Gpu"], 2 + (10 * 0.3), 2 + (10 * elapsed));
        Assert.Same(used, await span.StopAsync());
    }

    /// <summary>A power meter whose read fails or throws is left out, as a gap would count as no power, and a timer callback that throws would end the process.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SpanLeavesOutFailingPowerMeters()
    {
        var reads = 0;
        await using var span = new EnergySpan([], [new PowerSource("Gpu", () => ++reads == 1 ? 10 : null), new PowerSource("Cpu", () => throw new IOException("gone"))]);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        Assert.Null(await span.StopAsync());
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
