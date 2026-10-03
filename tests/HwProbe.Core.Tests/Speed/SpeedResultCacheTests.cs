using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Keys, saving and pruning in <see cref="SpeedResultCache"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedResultCacheTests : IDisposable
{
    private const string Command = "-i a.mkv -c:v h264_videotoolbox -b:v 8000000 -f null -";

    private static readonly SpeedTest _test = SpeedCatalog.Find("pattern|h264-8mbps")!;

    private static readonly SpeedOptions _speed = new(SpeedMethod.Quick, ["pattern"], ["h264-8mbps"], new SpeedSettings());

    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-results-").FullName;

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
        Assert.NotEqual(key, SpeedResultCache.Key("/usr/bin/ffmpeg", "ffmpeg version 8", HwType.videotoolbox, string.Empty, _test, Command, _speed));
        Assert.NotEqual(key, SpeedResultCache.Key("/usr/bin/ffmpeg", "ffmpeg version 7", HwType.none, string.Empty, _test, Command, _speed));
    }

    /// <summary>A saved measurement reads back, and pruning drops ones another ffmpeg made.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SavesReadsAndPrunes()
    {
        var ct = TestContext.Current.CancellationToken;
        var cache = new SpeedResultCache(_root);
        var result = new SpeedResult(HwType.videotoolbox, string.Empty, _test.Key, string.Empty, 240, 9, false, null);
        var measured = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(await cache.GetAsync("missing", ct));
        await cache.SaveAsync("current", new SpeedCacheEntry(measured, CapabilityReport.CurrentHwProbeVersion, "ffmpeg version 7", result), ct);
        await cache.SaveAsync("older", new SpeedCacheEntry(measured, CapabilityReport.CurrentHwProbeVersion, "ffmpeg version 6", result), ct);
        await File.WriteAllTextAsync(Path.Combine(_root, "broken.json"), "{", ct);

        var entry = await cache.GetAsync("current", ct);
        Assert.Equal((measured, 240.0, 9), (entry!.MeasuredUtc, entry.Result.Fps!.Value, entry.Result.Streams!.Value));
        Assert.Equal(2, cache.Prune("ffmpeg version 7"));
        Assert.NotNull(await cache.GetAsync("current", ct));
        Assert.Null(await cache.GetAsync("older", ct));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Returns the key for VideoToolbox with a fixed ffmpeg.</summary>
    /// <param name="command">The command.</param>
    /// <param name="speed">The run.</param>
    /// <returns>The key.</returns>
    private static string Key(string command, SpeedOptions speed) =>
        SpeedResultCache.Key("/usr/bin/ffmpeg", "ffmpeg version 7", HwType.videotoolbox, string.Empty, _test, command, speed);
}
