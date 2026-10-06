using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Cli.Tests;

/// <summary>Option parsing and defaults for <see cref="HwProbeCommand"/>.</summary>
[Trait("Category", "Unit")]
public sealed class HwProbeCommandTests
{
    /// <summary>A suite on its own measures at Confirm accuracy; with --speed, at the accuracy given.</summary>
    [Fact]
    public void SuiteBinds()
    {
        var alone = Bind(["--suite", "presets"]);
        var quick = Bind(["--suite", "tonemap", "--speed", "quick"]);

        Assert.Equal(("presets", Core.Speed.SpeedMethod.Confirm), (alone.Suite, alone.Speed!.Method));
        Assert.Equal(("tonemap", Core.Speed.SpeedMethod.Quick), (quick.Suite, quick.Speed!.Method));
        Assert.Null(Bind([]).Suite);
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
    [InlineData("--suite", "nope")]
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
        Assert.NotEmpty(new HwProbeCommand().Root.Parse(["--speed-repeats", "x"]).Errors);
        Assert.NotEmpty(new HwProbeCommand().Root.Parse(["--speed-time-limit", "x"]).Errors);
        Assert.Empty(new HwProbeCommand().Root.Parse(["--timeout", "30", "--speed-time-limit", "60"]).Errors);
    }

    /// <summary>Parses and binds a command line that must be valid.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The bound options.</returns>
    internal static CliOptions Bind(string[] args)
    {
        var command = new HwProbeCommand();
        var result = command.Root.Parse(args);
        Assert.Empty(result.Errors);
        return command.Bind(result);
    }
}
