using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Cli.Tests;

/// <summary>Defaults and bound paths of <see cref="HwProbeCommand"/>, which resolve against the host's filesystem.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class HwProbeCommandPathTests
{
    /// <summary>No arguments yields the documented defaults.</summary>
    [Fact]
    public void Defaults()
    {
        var options = HwProbeCommandTests.Bind([]);

        Assert.Null(options.FfmpegPath);
        Assert.Equal(StopStage.Matrix, options.StopAfter);
        Assert.Empty(options.Types);
        Assert.Equal(OutputFormat.Table, options.Format);
        Assert.Equal(TimeSpan.FromSeconds(15), options.ProbeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), options.FixtureTimeout);
        Assert.EndsWith(Path.Combine("hwprobe", "fixtures"), options.FixturesDirectory, StringComparison.Ordinal);
        Assert.False(options.ExpectHardware);
    }

    /// <summary>Every option binds.</summary>
    [Fact]
    public void AllOptionsBind()
    {
        var options = HwProbeCommandTests.Bind([
            "--ffmpeg", "/x/ffmpeg", "--stage", "devices", "--type", "vaapi,QSV", "--type", "nvenc", "--device", "/dev/dri/renderD129",
            "--format", "json", "--json", "/tmp/r.json", "--timeout", "5", "--fixture-timeout", "300", "--refresh",
            "--fixtures", "/tmp/fx", "--expect-hw", "--verbose",
        ]);

        Assert.Equal(
            new CliOptions(
                "/x/ffmpeg",
                StopStage.Devices,
                options.Types,
                "/dev/dri/renderD129",
                OutputFormat.Json,
                "/tmp/r.json",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(300),
                Refresh: true,
                Path.GetFullPath("/tmp/fx"),
                ExpectHardware: true,
                Verbose: true),
            options);
        Assert.Equal([HwType.qsv, HwType.nvenc, HwType.vaapi], options.Types.Order());
    }
}
