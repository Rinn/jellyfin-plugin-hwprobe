using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Audio transcodes from EncodingHelper 12.2.0's progressive audio command line, split at its input.</summary>
[Trait("Category", "Unit")]
[Collection(TestCollections.EncodingHelperEnvironment)]
public sealed class AudioArgumentsTests
{
    private static readonly AudioCell _flac = new("/m/a.flac", "flac", 2, 44100) { OutputCodec = "aac" };

    /// <summary>The input is the file alone, and the output upstream's threads, codec, bitrate, channels, and tags, without the file.</summary>
    [Fact]
    public void StereoAacIsUpstreams()
    {
        var args = Build(_flac);

        Assert.Equal("-i file:\"/m/a.flac\"", args.Input);
        Assert.Equal("-threads 0 -vn -ab 256000 -ac 2 -acodec aac -id3v2_version 3 -write_id3v1 1", args.Output);
    }

    /// <summary>A decode test gets the input alone.</summary>
    [Fact]
    public void DecodeHasNoOutput() =>
        Assert.Equal(string.Empty, Build(_flac with { OutputCodec = null }).Output);

    /// <summary>5.1 for AC-3 gets upstream's 640 kbps; a stereo input stays stereo; MP3 and Opus get their encoders.</summary>
    [Fact]
    public void CodecsGetTheirEncodersAndChannels()
    {
        var surround = _flac with { Channels = 6, SampleRate = 48000 };

        Assert.Contains("-ab 640000 -ac 6 -acodec ac3", Build(surround with { OutputCodec = "ac3", OutputChannels = 6 }).Output, StringComparison.Ordinal);
        Assert.Contains("-ac 2 -acodec ac3", Build(_flac with { OutputCodec = "ac3", OutputChannels = 6 }).Output, StringComparison.Ordinal);
        Assert.Contains("-acodec libmp3lame", Build(_flac with { OutputCodec = "mp3" }).Output, StringComparison.Ordinal);
        Assert.Contains("-acodec libopus", Build(_flac with { OutputCodec = "opus" }).Output, StringComparison.Ordinal);
    }

    /// <summary>VBR changes MP3's bitrate to LAME's quality setting; a 5.1 input downmixed to stereo gets the downmix filter.</summary>
    [Fact]
    public void SettingsReachTheCommand()
    {
        var mp3 = _flac with { OutputCodec = "mp3" };

        Assert.NotEqual(Build(mp3).Output, Build(mp3 with { AudioVbr = true }).Output);
        Assert.NotEqual(Build(_flac with { Channels = 6 }).Output, Build(_flac with { Channels = 6, DownmixAlgorithm = "Dave750" }).Output);
    }

    /// <summary>Generates audio arguments over the full test build.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The arguments.</returns>
    private static AudioArguments Build(AudioCell cell) => new ArgumentSource(TestCapabilities.Full, new CallRecorder()).BuildAudio(cell);
}
