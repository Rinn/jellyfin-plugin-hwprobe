using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>The wrapper <see cref="SpeedCommandLine"/> puts around upstream's arguments.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedCommandLineTests
{
    private static readonly ProbeArguments _args = new(
        "-hwaccel videotoolbox",
        " -filter_complex \"[1:0]scale[sub];[0:0][sub]overlay\"",
        "h264_videotoolbox",
        new Dictionary<string, string?>())
    {
        EncoderArgs = " -b:v 4000000",
        AudioArgs = " -codec:a:0 aac -ac 2 -ab 256000",
        InputArgument = "-hwaccel videotoolbox -i file:\"/c/a.mkv\" -i file:\"/c/s.sup\"",
        Threads = 0,
    };

    /// <summary>Every input loops, the run is bounded by duration, and upstream's encoder and audio arguments follow.</summary>
    [Fact]
    public void TranscodeLoopsEveryInput() =>
        Assert.Equal(
            "-hide_banner -v warning -nostats -progress pipe:1 -hwaccel videotoolbox -stream_loop -1 -i file:\"/c/a.mkv\" -stream_loop -1 -i file:\"/c/s.sup\" -t 12.5 -threads 0 -filter_complex \"[1:0]scale[sub];[0:0][sub]overlay\" -c:v h264_videotoolbox -b:v 4000000 -codec:a:0 aac -ac 2 -ab 256000 -f null -",
            SpeedCommandLine.Build(_args, TimeSpan.FromSeconds(12.5), decodeOnly: false));

    /// <summary>The command reads the same in every culture: dots in the duration and seek, plain digits.</summary>
    /// <param name="culture">The current culture.</param>
    [Theory]
    [MemberData(nameof(CultureScope.Different), MemberType = typeof(CultureScope))]
    public void SameInEveryCulture(string culture)
    {
        var invariant = SpeedCommandLine.Build(_args, TimeSpan.FromSeconds(12.5), decodeOnly: false, TimeSpan.FromSeconds(90.25));
        using var scope = new CultureScope(culture);

        Assert.Equal(invariant, SpeedCommandLine.Build(_args, TimeSpan.FromSeconds(12.5), decodeOnly: false, TimeSpan.FromSeconds(90.25)));
        Assert.Contains("-ss 90.25 ", invariant, StringComparison.Ordinal);
    }

    /// <summary>A decode test keeps only the input and drops audio.</summary>
    [Fact]
    public void DecodeKeepsOnlyTheInput() =>
        Assert.Equal(
            "-hide_banner -v warning -nostats -progress pipe:1 -stream_loop -1 -i file:\"/c/a.mkv\" -t 10 -an -f null -",
            SpeedCommandLine.Build(_args with { InputArgument = " -i file:\"/c/a.mkv\"" }, TimeSpan.FromSeconds(10), decodeOnly: true));

    /// <summary>A path holding <c> -i </c> or an escaped quote is left alone; only real input options loop.</summary>
    [Fact]
    public void PathsAreNotTakenForOptions() =>
        Assert.Equal(
            "-hide_banner -v warning -nostats -progress pipe:1 -stream_loop -1 -i file:\"/m/Foo -i \\\" Bar.mkv\" -t 10 -an -f null -",
            SpeedCommandLine.Build(_args with { InputArgument = "-i file:\"/m/Foo -i \\\" Bar.mkv\"" }, TimeSpan.FromSeconds(10), decodeOnly: true));

    /// <summary>A start point seeks the first input only.</summary>
    [Fact]
    public void StartSeeksTheVideo() =>
        Assert.Contains(
            "-hwaccel videotoolbox -ss 720 -stream_loop -1 -i file:\"/c/a.mkv\" -stream_loop -1 -i file:\"/c/s.sup\"",
            SpeedCommandLine.Build(_args, TimeSpan.FromSeconds(10), decodeOnly: false, TimeSpan.FromMinutes(12)),
            StringComparison.Ordinal);

    /// <summary>Arguments generated without the input can't be measured.</summary>
    [Fact]
    public void InputIsRequired() =>
        Assert.Throws<ArgumentException>(() => SpeedCommandLine.Build(_args with { InputArgument = null }, TimeSpan.FromSeconds(10), decodeOnly: false));
}
