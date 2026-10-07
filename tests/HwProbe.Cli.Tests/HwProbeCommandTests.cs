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

        Assert.NotNull(alone.Speed);
        Assert.NotNull(quick.Speed);
        Assert.Equal(("presets", Core.Speed.SpeedMethod.Confirm), (alone.Suite, alone.Speed.Method));
        Assert.Equal(("tonemap", Core.Speed.SpeedMethod.Quick), (quick.Suite, quick.Speed.Method));
        Assert.Null(Bind([]).Suite);
    }

    /// <summary>Audio inputs alone measure no default video and every audio output; named videos and outputs are kept; audio without software is an error.</summary>
    [Fact]
    public void AudioBinds()
    {
        var alone = Bind(["--speed", "quick", "--speed-audio", "flac,ac3"]).Speed;
        var named = Bind(["--speed", "quick", "--speed-audio", "flac", "--speed-videos", "pattern", "--speed-outputs", "h264-8mbps,audio-mp3"]).Speed;

        Assert.NotNull(alone);
        Assert.NotNull(named);
        Assert.Empty(alone.Videos);
        Assert.Equal(["flac", "ac3"], alone.Audios);
        Assert.Equal(Core.Speed.SpeedCatalog.Outputs.Where(o => o.Audio).Select(o => o.Key), alone.Outputs);
        Assert.Equal(["pattern"], named.Videos);
        Assert.Equal(["h264-8mbps", "audio-mp3"], named.Outputs);
        Assert.NotEmpty(new HwProbeCommand().Root.Parse(["--speed", "--speed-audio", "flac", "--speed-backends", "nvenc"]).Errors);
        Assert.NotEmpty(new HwProbeCommand().Root.Parse(["--speed", "--speed-audio", "dsd"]).Errors);
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
