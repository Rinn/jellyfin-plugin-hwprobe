using Jellyfin.Plugin.HwProbe.Core.Report;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Report;

/// <summary>On-disk caching in <see cref="ReportStore"/>.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class ReportStoreCacheTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hwprobe-report-").FullName;

    /// <summary>A cached report is found by its fingerprint and not by another.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CacheHitsOnlyMatchingFingerprint()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new ReportStore(_directory);
        await store.SaveCachedAsync(ReportStoreTests.Sample("sha256:abc"), ct);

        Assert.NotNull(await store.LoadCachedAsync("sha256:abc", ct));
        Assert.Null(await store.LoadCachedAsync("sha256:def", ct));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
