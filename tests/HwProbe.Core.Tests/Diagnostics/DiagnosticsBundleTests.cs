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
            "# ffmpeg -hide_banner -v debug -i /home/alex/fixtures/h264.mp4 -c:v h264_vaapi -f null -\n# env: LC_ALL=C LIBVA_DRIVER_NAME=iHD -OCL_ICD_VENDORS\n# probe: vaapi:/dev/dri/renderD128:Smoke:h264 Pass\n# result: exit 0, 10 frames, 0.50 s\n[h264 @ 0x1] Using VAAPI\n",
            entries["stderr/002-vaapi__dev_dri_renderD128_Smoke_h264.txt"]);
    }

    /// <summary>The README describes the run and links the issue form.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReadmeDescribesTheRun()
    {
        var entries = await EntriesAsync(Sample(), []);

        Assert.Contains("ffmpeg 8.1.2 (/usr/lib/jellyfin-ffmpeg/ffmpeg)", entries["README.txt"], StringComparison.Ordinal);
        Assert.Contains(IssueLink.For(Sample(), null), entries["README.txt"], StringComparison.Ordinal);
    }

    /// <summary>The issue link fills the form's fields from the report, escaped for a URL.</summary>
    [Fact]
    public void IssueLinkFillsTheForm()
    {
        var link = IssueLink.For(Sample(), "12.1.0");

        Assert.StartsWith(IssueLink.Form + "&os=", link, StringComparison.Ordinal);
        Assert.Contains("&jellyfin=12.1.0", link, StringComparison.Ordinal);
        Assert.Contains("&ffmpeg=8.1.2%2C%20jellyfin-ffmpeg", link, StringComparison.Ordinal);
        Assert.Contains("&ffmpeg=9.0.2%2C%20not%20jellyfin-ffmpeg", IssueLink.For(Sample() with { Ffmpeg = new FfmpegSummary("/opt/homebrew/bin/ffmpeg", "SystemPath", "9.0.2", IsJellyfinBuild: false) }, null), StringComparison.Ordinal);
        Assert.DoesNotContain("&jellyfin=", IssueLink.For(Sample(), null), StringComparison.Ordinal);
        Assert.DoesNotContain("&gpu=", link, StringComparison.Ordinal);
    }

    /// <summary>The GPU field names each GPU as the system does: the Windows adapter's name, the model Mesa's driver line gives, else the maker, PCI IDs, and driver line; the software adapter is left out.</summary>
    /// <param name="device">The device.</param>
    /// <param name="vendor">Its PCI vendor ID.</param>
    /// <param name="id">Its PCI device ID.</param>
    /// <param name="name">Its name or driver line.</param>
    /// <param name="expected">The field, or null when it isn't filled.</param>
    [Theory]
    [InlineData("dx11:0", "0x10de", "0x2c02", "NVIDIA GeForce RTX 5080", "NVIDIA GeForce RTX 5080")]
    [InlineData("/dev/dri/renderD128", "0x1002", "0x7480", "Mesa Gallium driver 24.0.5 for AMD Radeon RX 7600 (radeonsi, navi33, LLVM 17.0.6, DRM 3.57, 6.8.0-45-generic)", "AMD Radeon RX 7600 (1002:7480)")]
    [InlineData("/dev/dri/renderD128", "0x8086", "0x5a85", "Intel iHD driver for Intel(R) Gen Graphics - 24.1.0 (1b5e662)", "Intel 8086:5a85, Intel iHD driver for Intel(R) Gen Graphics - 24.1.0")]
    [InlineData("/dev/dri/renderD129", "0x10de", "0x2c02", null, "NVIDIA 10de:2c02")]
    [InlineData("dx11:2", "0x1414", "0x008c", "Microsoft Basic Render Driver", null)]
    public void IssueLinkNamesTheGpu(string device, string vendor, string id, string? name, string? expected)
    {
        var link = IssueLink.For(Sample() with { Gpus = [new GpuInfo(device, vendor, id, name)] }, null);

        Assert.Equal(expected is null ? null : "&gpu=" + Uri.EscapeDataString(expected), System.Text.RegularExpressions.Regex.Match(link, "&gpu=[^&]*") is { Success: true } m ? m.Value : null);
    }

    /// <summary>The report inside a bundle reads back; anything that isn't a bundle reads as null.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReportReadsBack()
    {
        using var stream = new MemoryStream();
        await DiagnosticsBundle.WriteAsync(stream, Sample(), [], TestContext.Current.CancellationToken);

        Assert.Equal(Sample().GeneratedUtc, (await DiagnosticsBundle.ReadReportAsync(stream.ToArray(), TestContext.Current.CancellationToken))?.GeneratedUtc);
        Assert.Null(await DiagnosticsBundle.ReadReportAsync([1, 2, 3], TestContext.Current.CancellationToken));
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
        await DiagnosticsBundle.WriteAsync(stream, report, runs, TestContext.Current.CancellationToken);
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
