using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>The tests <see cref="SpeedFileTests"/> offers for a real file, and the cell built from one.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedFileTestsTests
{
    private static readonly SpeedFile _film = new("/m/film.mkv", "Film", TimeSpan.FromMinutes(120), new SpeedFileVideo(0, "hevc", 10, 3840, 2160, 23.976f) { ColorTransfer = "smpte2084", ColorPrimaries = "bt2020", ColorSpace = "bt2020nc", Profile = "Main 10" })
    {
        MediaSourceId = "abc",
        Audio = new SpeedFileAudio(1, "eac3", 6),
        Subtitles = [new SpeedFileSubtitle(2, "PGSSUB", false, "eng"), new SpeedFileSubtitle(3, "subrip", true, null) { ExternalPath = "/m/film.en.srt" }],
    };

    /// <summary>A 4K HDR file offers 720p and 1080p H.264, HEVC, AV1, decoding, and each subtitle track, tone-mapped.</summary>
    [Fact]
    public void FileOffersTheUsualTargets()
    {
        var tests = SpeedFileTests.For(_film);

        Assert.Equal(["file-720p-h264", "file-1080p-h264", "file-720p-hevc", "file-720p-av1", "file-decode", "file-subs-2", "file-subs-3"], tests.Select(t => t.Key));
        Assert.Equal("Film to 1080p H.264, tone-mapped", tests[1].Label);
        Assert.Equal("Film to 720p H.264, eng (PGSSUB) subtitles burned in", tests[5].Label);
        Assert.All(tests, t => Assert.Equal(TimeSpan.FromMinutes(12), t.StartAt));
    }

    /// <summary>A 720p file isn't offered an upscale to 1080p.</summary>
    [Fact]
    public void NoUpscale() =>
        Assert.DoesNotContain(SpeedFileTests.For(_film with { Video = _film.Video with { Width = 1280, Height = 720 } }), t => t.Key == "file-1080p-h264");

    /// <summary>The cell carries the file's own streams: indices, audio, an internal image subtitle and an external text one.</summary>
    [Fact]
    public void CellCarriesTheFilesStreams()
    {
        var tests = SpeedFileTests.For(_film);
        var settings = new SpeedSettings();

        var image = SpeedVariants.Base(tests[5], settings, new Dictionary<string, string>());
        var text = SpeedVariants.Base(tests[6], settings, new Dictionary<string, string>());
        var untoned = SpeedVariants.Base(tests[0], settings with { Tonemap = false }, new Dictionary<string, string>());

        Assert.Equal(("hevc", 10, "/m/film.mkv", "abc", true), (image.InputCodec, image.BitDepth, image.SourcePath, image.MediaSourceId, image.Tonemap));
        Assert.Equal((1, "eac3", 6), (image.AudioIndex, image.AudioCodec, image.AudioChannels));
        Assert.Equal((2, "PGSSUB"), (image.InternalSubtitleIndex!.Value, image.InternalSubtitleCodec));
        Assert.Equal("/m/film.en.srt", text.SubtitlePath);
        Assert.Null(text.InternalSubtitleIndex);
        Assert.False(untoned.Tonemap);
    }

    /// <summary>A file's tests resolve alongside the generated ones, in the order asked.</summary>
    [Fact]
    public void OptionsResolveFileTests()
    {
        var options = new SpeedOptions(SpeedMethod.Quick, ["file-decode", "1080p-h264", "nope"], SpeedComparison.None, new SpeedSettings()) { File = _film };

        Assert.Equal(["file-decode", "1080p-h264"], options.Resolve().Select(t => t.Key));
    }
}
