using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Report;

/// <summary>JSON shape and caching of <see cref="ReportStore"/>.</summary>
[Trait("Category", "Unit")]
public sealed class ReportStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hwprobe-report-").FullName;

    /// <summary>A report round-trips and carries schemaVersion, string enums and camelCase.</summary>
    [Fact]
    public void RoundTripsWithDocumentedShape()
    {
        var report = Sample("sha256:abc");

        var json = ReportStore.Serialize(report);
        var back = ReportStore.Deserialize(json);

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"verdict\": \"Viable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"qsv\"", json, StringComparison.Ordinal);
        Assert.Contains("\"hevc10\": \"Pass\"", json, StringComparison.Ordinal);
        Assert.NotNull(back);
        Assert.Equal(json, ReportStore.Serialize(back));
    }

    /// <summary>Another schema version or broken JSON reads as null.</summary>
    /// <param name="json">The input.</param>
    [Theory]
    [InlineData("{\"schemaVersion\": 2}")]
    [InlineData("not json")]
    public void IncompatibleInputIsNull(string json) => Assert.Null(ReportStore.Deserialize(json));

    /// <summary>A cached report is found by its fingerprint and not by another.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CacheHitsOnlyMatchingFingerprint()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new ReportStore(_directory);
        await store.SaveCachedAsync(Sample("sha256:abc"), ct);

        Assert.NotNull(await store.LoadCachedAsync("sha256:abc", ct));
        Assert.Null(await store.LoadCachedAsync("sha256:def", ct));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Builds a small report.</summary>
    /// <param name="fingerprint">Its fingerprint.</param>
    /// <returns>The report.</returns>
    private static CapabilityReport Sample(string fingerprint) => new(
        CapabilityReport.CurrentSchemaVersion,
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        fingerprint,
        new FfmpegSummary("/usr/lib/jellyfin-ffmpeg/ffmpeg", "KnownPath", "7.1.4", IsJellyfinBuild: true),
        new HostSummary("linux", "6.8.0", "docker"),
        new StageASummary(
            ["vaapi", "qsv"],
            new Dictionary<HwType, BuildStatus> { [HwType.qsv] = BuildStatus.Selectable, [HwType.amf] = BuildStatus.NotBuilt },
            new Dictionary<string, bool> { ["tonemap_opencl.bt2390"] = true }),
        [
            new BackendReport(
                HwType.qsv,
                "/dev/dri/renderD128",
                BackendVerdict.Viable,
                PipelineTier.FullOpencl,
                new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["hevc10"] = ProbeOutcome.Pass },
                new Dictionary<string, ProbeOutcome> { ["h264_lowpower"] = ProbeOutcome.Pass },
                new Dictionary<string, ProbeOutcome> { ["opencl"] = ProbeOutcome.Pass },
                string.Empty),
        ],
        [new Finding(FindingSeverity.Warn, "legacy-copyback", "Install intel-opencl-icd.")],
        [new ProbeResult("qsv:/dev/dri/renderD128:Smoke:h264", HwType.qsv, "/dev/dri/renderD128", "h264", ProbeStage.Smoke, ProbeOutcome.Pass, 120.5, TimeSpan.FromSeconds(1), string.Empty, string.Empty)]);
}
