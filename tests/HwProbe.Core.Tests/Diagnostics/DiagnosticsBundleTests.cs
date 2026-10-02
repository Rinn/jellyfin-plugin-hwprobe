using System.IO.Compression;
using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Diagnostics;

/// <summary>Layout and contents of a <see cref="DiagnosticsBundle"/> zip.</summary>
[Trait("Category", "Unit")]
public sealed class DiagnosticsBundleTests
{
    private const string Smoke = "-hide_banner -v debug -i /home/alex/fixtures/h264.mp4 -c:v h264_vaapi -f null -";

    private static readonly DiagnosticsScrubber _scrubber = new([("/home/alex", "~")], [("alex", "<user>")]);

    /// <summary>Listings go under ffmpeg/ by corpus name; launches go under stderr/ in order, named by probe, with a header.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LaysOutLikeTheCorpus()
    {
        RecordedRun[] runs =
        [
            Run("-version", "ffmpeg version 8.1.2-Jellyfin\n", string.Empty),
            Run("-h filter=scale_cuda", "Filter scale_cuda\n", string.Empty),
            Run("-y -f lavfi -i testsrc /home/alex/fixtures/h264.mp4", string.Empty, "building\n"),
            Run(Smoke, string.Empty, "[h264 @ 0x1] Using VAAPI\n", new Dictionary<string, string?> { ["LIBVA_DRIVER_NAME"] = "iHD", ["OCL_ICD_VENDORS"] = null }),
        ];

        var entries = await EntriesAsync(Sample(), runs);

        Assert.Equal(["README.txt", "report.json", "ffmpeg/version.txt", "ffmpeg/h-filter-scale_cuda.txt", "stderr/001-launch.txt", "stderr/002-vaapi__dev_dri_renderD128_Smoke_h264.txt"], entries.Keys);
        Assert.Equal("ffmpeg version 8.1.2-Jellyfin\n", entries["ffmpeg/version.txt"]);
        Assert.Equal(
            "# ffmpeg -hide_banner -v debug -i ~/fixtures/h264.mp4 -c:v h264_vaapi -f null -\n# env: LIBVA_DRIVER_NAME=iHD -OCL_ICD_VENDORS\n# probe: vaapi:/dev/dri/renderD128:Smoke:h264 Pass\n# result: exit 0, 10 frames, 0.50 s\n[h264 @ 0x1] Using VAAPI\n",
            entries["stderr/002-vaapi__dev_dri_renderD128_Smoke_h264.txt"]);
    }

    /// <summary>The report and README are scrubbed and the README links the issue form.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReportAndReadmeAreScrubbed()
    {
        var report = Sample() with { Ffmpeg = new FfmpegSummary("/home/alex/ffmpeg", "CommandLine", "8.1.2", IsJellyfinBuild: true) };

        var entries = await EntriesAsync(report, []);

        Assert.Contains("\"path\": \"~/ffmpeg\"", entries["report.json"], StringComparison.Ordinal);
        Assert.Contains("ffmpeg 8.1.2 (~/ffmpeg)", entries["README.txt"], StringComparison.Ordinal);
        Assert.Contains(DiagnosticsBundle.IssueUrl, entries["README.txt"], StringComparison.Ordinal);
        Assert.DoesNotContain(entries.Values, v => v.Contains("alex", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A timed-out launch says so in its header.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TimeoutIsRecorded()
    {
        var run = new RecordedRun(new FfmpegInvocation("ffmpeg", "-init_hw_device vaapi", new Dictionary<string, string?>(), TimeSpan.FromSeconds(15)), new FfmpegRunResult(FfmpegRunStatus.TimedOut, null, string.Empty, string.Empty, null, TimeSpan.FromSeconds(15), null));

        var entries = await EntriesAsync(Sample(), [run]);

        Assert.Contains("# result: timed out, 15.00 s\n", entries["stderr/001-launch.txt"], StringComparison.Ordinal);
    }

    /// <summary>Writes a bundle to memory and reads every entry back.</summary>
    /// <param name="report">The report.</param>
    /// <param name="runs">The launches.</param>
    /// <returns>Entry contents by name, in zip order.</returns>
    private static async Task<Dictionary<string, string>> EntriesAsync(CapabilityReport report, RecordedRun[] runs)
    {
        using var stream = new MemoryStream();
        await DiagnosticsBundle.WriteAsync(stream, report, runs, _scrubber, TestContext.Current.CancellationToken);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(await entry.OpenAsync(TestContext.Current.CancellationToken));
            entries[entry.FullName] = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        }

        return entries;
    }

    /// <summary>A launch that exited 0 after 10 frames.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="stdout">Its stdout.</param>
    /// <param name="stderr">Its stderr.</param>
    /// <param name="environment">Its environment overrides, or none.</param>
    /// <returns>The launch.</returns>
    private static RecordedRun Run(string arguments, string stdout, string stderr, Dictionary<string, string?>? environment = null) =>
        new(new FfmpegInvocation("ffmpeg", arguments, environment ?? [], TimeSpan.FromSeconds(15)), new FfmpegRunResult(FfmpegRunStatus.Exited, 0, stdout, stderr, 10, TimeSpan.FromSeconds(0.5), null));

    /// <summary>A report with one probe whose command line is <see cref="Smoke"/>.</summary>
    /// <returns>The report.</returns>
    private static CapabilityReport Sample() => new(
        CapabilityReport.CurrentSchemaVersion,
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        "sha256:abc",
        new FfmpegSummary("/usr/lib/jellyfin-ffmpeg/ffmpeg", "KnownPath", "8.1.2", IsJellyfinBuild: true),
        new HostSummary("linux", "6.8.0", null),
        new StageASummary(["vaapi"], new Dictionary<HwType, BuildStatus>(), new Dictionary<string, bool>()),
        [],
        [],
        [new ProbeResult("vaapi:/dev/dri/renderD128:Smoke:h264", HwType.vaapi, "/dev/dri/renderD128", "h264", ProbeStage.Smoke, ProbeOutcome.Pass, null, TimeSpan.FromSeconds(0.5), string.Empty, string.Empty) { CommandLine = Smoke }]);
}
