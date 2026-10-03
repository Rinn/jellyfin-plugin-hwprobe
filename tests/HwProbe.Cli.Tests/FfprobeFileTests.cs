using Xunit;

namespace Jellyfin.Plugin.HwProbe.Cli.Tests;

/// <summary>Reading ffprobe's JSON in <see cref="FfprobeFile"/>.</summary>
[Trait("Category", "Unit")]
public sealed class FfprobeFileTests
{
    /// <summary>Video, audio and subtitles map to Jellyfin's names; cover art is skipped.</summary>
    [Fact]
    public void ParsesStreams()
    {
        const string Json = """
            {"streams":[
              {"index":0,"codec_name":"mjpeg","codec_type":"video","width":600,"height":900,"disposition":{"attached_pic":1}},
              {"index":1,"codec_name":"hevc","profile":"Main 10","codec_type":"video","width":3840,"height":2160,"pix_fmt":"yuv420p10le","color_transfer":"smpte2084","color_primaries":"bt2020","color_space":"bt2020nc","field_order":"progressive","avg_frame_rate":"24000/1001","disposition":{"attached_pic":0}},
              {"index":2,"codec_name":"eac3","codec_type":"audio","channels":6},
              {"index":3,"codec_name":"hdmv_pgs_subtitle","codec_type":"subtitle","tags":{"language":"eng"}},
              {"index":4,"codec_name":"subrip","codec_type":"subtitle"}],
             "format":{"duration":"5400.5"}}
            """;

        var file = FfprobeFile.Parse("/m/Film (2020).mkv", Json);

        Assert.Equal(("Film (2020)", 1, "hevc", 10, 3840, 2160), (file.Name, file.Video.Index, file.Video.Codec, file.Video.BitDepth, file.Video.Width, file.Video.Height));
        Assert.Equal(23.976, file.Video.FrameRate, 3);
        Assert.True(file.Video.IsHdr);
        Assert.False(file.Video.Interlaced);
        Assert.Equal(TimeSpan.FromSeconds(5400.5), file.Duration);
        Assert.Equal((2, "eac3", 6), (file.Audio!.Index, file.Audio.Codec, file.Audio.Channels));
        Assert.Equal([(3, "PGSSUB", false, "eng"), (4, "subrip", true, null)], file.Subtitles.Select(s => (s.Index, s.Codec, s.IsText, s.Title)));
    }

    /// <summary>A file with no video is refused.</summary>
    [Fact]
    public void NoVideoIsRefused() =>
        Assert.Throws<InvalidOperationException>(() => FfprobeFile.Parse("/m/a.flac", """{"streams":[{"index":0,"codec_name":"flac","codec_type":"audio","channels":2}]}"""));
}
