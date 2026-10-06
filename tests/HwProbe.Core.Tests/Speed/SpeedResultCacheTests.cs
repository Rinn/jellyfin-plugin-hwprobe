using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Keys in <see cref="SpeedResultCache"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedResultCacheTests
{
    private const string Command = "-i a.mkv -c:v h264_videotoolbox -b:v 8000000 -f null -";

    private static readonly SpeedTest _test = SpeedCatalog.Find("pattern|h264-8mbps")!;

    private static readonly SpeedOptions _speed = new(SpeedMethod.Quick, ["pattern"], ["h264-8mbps"], new SpeedSettings());

    /// <summary>The same inputs give the same key; a change to the command, backend, ffmpeg, accuracy or repeats gives another, the time limit doesn't.</summary>
    [Fact]
    public void KeyFollowsWhatIsMeasured()
    {
        var key = Key(Command, _speed);

        Assert.Equal(key, Key(Command, _speed));
        Assert.Equal(key, Key(Command, _speed with { TimeLimit = TimeSpan.FromMinutes(1) }));
        Assert.NotEqual(key, Key(Command + " -preset slow", _speed));
        Assert.NotEqual(key, Key(Command, _speed with { Repeats = 3 }));
        Assert.NotEqual(key, Key(Command, _speed with { Method = SpeedMethod.Confirm }));
        Assert.NotEqual(key, Key(Command, _speed with { MeasureResources = !_speed.MeasureResources }));
        Assert.NotEqual(key, SpeedResultCache.Key("/usr/bin/ffmpeg", "ffmpeg version 8", HwType.videotoolbox, string.Empty, _test, Command, _speed));
        Assert.NotEqual(key, SpeedResultCache.Key("/usr/bin/ffmpeg", "ffmpeg version 7", HwType.none, string.Empty, _test, Command, _speed));
    }

    /// <summary>Returns the key for VideoToolbox with a fixed ffmpeg.</summary>
    /// <param name="command">The command.</param>
    /// <param name="speed">The run.</param>
    /// <returns>The key.</returns>
    private static string Key(string command, SpeedOptions speed) =>
        SpeedResultCache.Key("/usr/bin/ffmpeg", "ffmpeg version 7", HwType.videotoolbox, string.Empty, _test, command, speed);
}
