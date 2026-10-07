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

    /// <summary>An image run keeps the server's arguments, bounds and seeks the input rather than the output, discards the images, and repeats the source through a concat list.</summary>
    [Fact]
    public void ImagesBoundTheInput()
    {
        var images = new ProbeArguments("-hwaccel videotoolbox", "-vf \"setpts=N/24.000/TB,fps=0.1,scale=320:-2\"", "mjpeg", new Dictionary<string, string?>())
        {
            InputArgument = "-skip_frame nokey -hwaccel videotoolbox -i file:\"/c/a.mkv\" -map 0:0",
            EncoderArgs = "-qscale:v 4 -fps_mode passthrough",
            Threads = 1,
        };

        Assert.Equal(
            "-hide_banner -v warning -nostats -progress pipe:1 -skip_frame nokey -hwaccel videotoolbox -ss 60 -t 10 -i file:\"/c/a.mkv\" -map 0:0 -an -sn -vf \"setpts=N/24.000/TB,fps=0.1,scale=320:-2\" -threads 1 -c:v mjpeg -qscale:v 4 -fps_mode passthrough -f null -",
            SpeedCommandLine.BuildImages(images, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1)));
        Assert.StartsWith(
            "-hide_banner -v warning -nostats -progress pipe:1 -skip_frame nokey -hwaccel videotoolbox -t 600 -f concat -safe 0 -i file:\"/t/l\\\".txt\" -map 0:0 -an",
            SpeedCommandLine.BuildImages(images with { InputArgument = "-skip_frame nokey -hwaccel videotoolbox -i file:\"/c/a \\\" b.mkv\" -map 0:0" }, TimeSpan.FromMinutes(10), loopList: "/t/l\".txt"),
            StringComparison.Ordinal);
        Assert.Equal("file '/m/it'\\''s.mkv'\nfile '/m/it'\\''s.mkv'\n", SpeedCommandLine.LoopList("/m/it's.mkv", 2, null));
        Assert.Equal("file '/c/a.webm'\nduration 10.9\n", SpeedCommandLine.LoopList("/c/a.webm", 1, 10.9));
        Assert.Throws<ArgumentException>(() => SpeedCommandLine.BuildImages(images with { Threads = null }, TimeSpan.FromSeconds(10)));
    }

    /// <summary>An audio run loops the input and bounds the output; a decode run keeps the input alone and drops any video.</summary>
    [Fact]
    public void AudioBoundsTheOutput()
    {
        var aac = new AudioArguments("-i file:\"/c/a.flac\"", "-threads 0 -vn -ab 256000 -ac 2 -acodec aac -id3v2_version 3 -write_id3v1 1", new Dictionary<string, string?>());

        Assert.Equal(
            "-hide_banner -v warning -nostats -progress pipe:1 -stream_loop -1 -i file:\"/c/a.flac\" -t 60 -threads 0 -vn -ab 256000 -ac 2 -acodec aac -id3v2_version 3 -write_id3v1 1 -f null -",
            SpeedCommandLine.BuildAudio(aac, TimeSpan.FromMinutes(1)));
        Assert.Equal(
            "-hide_banner -v warning -nostats -progress pipe:1 -stream_loop -1 -i file:\"/c/a.flac\" -t 60 -vn -f null -",
            SpeedCommandLine.BuildAudio(aac with { Output = string.Empty }, TimeSpan.FromMinutes(1)));
    }

    /// <summary>Arguments generated without the input can't be measured.</summary>
    [Fact]
    public void InputIsRequired() =>
        Assert.Throws<ArgumentException>(() => SpeedCommandLine.Build(_args with { InputArgument = null }, TimeSpan.FromSeconds(10), decodeOnly: false));
}
