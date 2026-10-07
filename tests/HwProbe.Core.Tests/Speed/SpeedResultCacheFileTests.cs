using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Saving and pruning on disk in <see cref="SpeedResultCache"/>.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class SpeedResultCacheFileTests : IDisposable
{
    private static readonly SpeedTest _test = SpeedCatalog.Find("pattern|h264-8mbps") ?? throw new InvalidOperationException("The catalog has no pattern|h264-8mbps test.");

    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-results-").FullName;

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
        await cache.SaveAsync("current", new SpeedCacheEntry(measured, SpeedResultCache.MeasurementVersion, "ffmpeg version 7", result), ct);
        await cache.SaveAsync("older", new SpeedCacheEntry(measured, SpeedResultCache.MeasurementVersion, "ffmpeg version 6", result), ct);
        await File.WriteAllTextAsync(Path.Combine(_root, "broken.json"), "{", ct);

        var entry = await cache.GetAsync("current", ct);
        Assert.NotNull(entry);
        Assert.Equal((measured, (double?)240.0, (int?)9), (entry.MeasuredUtc, entry.Result.Fps, entry.Result.Streams));
        Assert.Equal(2, cache.Prune("ffmpeg version 7"));
        Assert.NotNull(await cache.GetAsync("current", ct));
        Assert.Null(await cache.GetAsync("older", ct));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
