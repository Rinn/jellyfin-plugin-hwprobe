using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Speed;
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
    };

    /// <summary>Every input loops, the run is bounded by duration, and upstream's encoder and audio arguments follow.</summary>
    [Fact]
    public void TranscodeLoopsEveryInput() =>
        Assert.Equal(
            "-hide_banner -v warning -nostats -progress pipe:1 -hwaccel videotoolbox -stream_loop -1 -i file:\"/c/a.mkv\" -stream_loop -1 -i file:\"/c/s.sup\" -t 12.5 -filter_complex \"[1:0]scale[sub];[0:0][sub]overlay\" -c:v h264_videotoolbox -b:v 4000000 -codec:a:0 aac -ac 2 -ab 256000 -f null -",
            SpeedCommandLine.Build(_args, TimeSpan.FromSeconds(12.5), decodeOnly: false));

    /// <summary>A decode test keeps only the input and drops audio.</summary>
    [Fact]
    public void DecodeKeepsOnlyTheInput() =>
        Assert.Equal(
            "-hide_banner -v warning -nostats -progress pipe:1 -stream_loop -1 -i file:\"/c/a.mkv\" -t 10 -an -f null -",
            SpeedCommandLine.Build(_args with { InputArgument = " -i file:\"/c/a.mkv\"" }, TimeSpan.FromSeconds(10), decodeOnly: true));

    /// <summary>Arguments generated without the input can't be measured.</summary>
    [Fact]
    public void InputIsRequired() =>
        Assert.Throws<ArgumentException>(() => SpeedCommandLine.Build(_args with { InputArgument = null }, TimeSpan.FromSeconds(10), decodeOnly: false));
}
