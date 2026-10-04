using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Cli.Tests;

/// <summary>Option parsing and defaults for <see cref="HwProbeCommand"/>.</summary>
[Trait("Category", "Unit")]
public sealed class HwProbeCommandTests
{
    /// <summary>No arguments yields the documented defaults.</summary>
    [Fact]
    public void Defaults()
    {
        var options = Bind([]);

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
        var options = Bind([
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

    /// <summary>Invalid values are parse errors, not exceptions.</summary>
    /// <param name="args">The command line.</param>
    [Theory]
    [InlineData("--type", "cuda")]
    [InlineData("--type", "none")]
    [InlineData("--stage", "B")]
    [InlineData("--timeout", "0")]
    [InlineData("--fixture-timeout", "-1")]
    [InlineData("--format", "xml")]
    [InlineData("--bogus")]
    public void InvalidValuesAreErrors(params string[] args)
    {
        var result = new HwProbeCommand().Root.Parse(args);

        Assert.NotEmpty(result.Errors);
    }

    /// <summary>Numbers parse the same in every culture, and a bad one is an error rather than a crash.</summary>
    /// <param name="culture">The current culture.</param>
    [Theory]
    [MemberData(nameof(CultureScope.Different), MemberType = typeof(CultureScope))]
    public void NumbersParseInEveryCulture(string culture)
    {
        using var scope = new CultureScope(culture);

        Assert.NotEmpty(new HwProbeCommand().Root.Parse(["--fixture-timeout", "-1"]).Errors);
        Assert.NotEmpty(new HwProbeCommand().Root.Parse(["--timeout", "x"]).Errors);
        Assert.Empty(new HwProbeCommand().Root.Parse(["--timeout", "30", "--speed-time-limit", "60"]).Errors);
    }

    /// <summary>Parses and binds a command line that must be valid.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The bound options.</returns>
    private static CliOptions Bind(string[] args)
    {
        var command = new HwProbeCommand();
        var result = command.Root.Parse(args);
        Assert.Empty(result.Errors);
        return command.Bind(result);
    }
}
