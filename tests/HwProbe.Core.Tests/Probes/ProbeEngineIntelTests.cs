using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Tests.Devices;
using Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>An Intel VAAPI host with an OpenCL-enabled jellyfin-ffmpeg build.</summary>
[Trait("Category", "FakeFfmpeg")]
[Trait("Category", "Platform")]
public sealed class ProbeEngineIntelTests : IDisposable
{
    private const string Node = "/dev/dri/renderD128";
    private const string IntelDriver = "[VAAPI @ 0x1] VAAPI driver: Intel iHD driver for Intel(R) Gen Graphics - 26.3.5 (1b5e662).\n";

    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-opencl-").FullName;
    private readonly EngineRunner _runner = new("jellyfin-8.1.2-linux-amd64");

    /// <summary>A missing OpenCL runtime, or one that aborts, keeps the tier upstream picks, warns, and gives tone-map failures the OpenCL remedy.</summary>
    /// <param name="aborts">Whether OpenCL aborts rather than failing with a message.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRuntimeWarnsWithoutChangingTier(bool aborts)
    {
        var report = await RunAsync(openclStarts: false, openclAborts: aborts);

        var vaapi = Assert.Single(report.Backends);
        Assert.Equal(PipelineTier.FullOpencl, vaapi.Tier);
        Assert.Contains(report.Findings, f => f.Code == "opencl-unavailable" && f.Severity == FindingSeverity.Warn);
        Assert.Equal(ProbeOutcome.DeviceUnavailable, Assert.Single(report.Probes, p => p.Stage == ProbeStage.Tier).Outcome);
        var tonemap = Assert.Single(report.Probes, p => p.ProbeId.EndsWith(":Tonemap:hevc_10bit", StringComparison.Ordinal));
        Assert.Equal(Hints.OpenclUnavailable(inContainer: false), tonemap.Hint);
        Assert.NotEqual(Hints.OpenclUnavailable(inContainer: false), Assert.Single(report.Probes, p => p.ProbeId.EndsWith(":Tonemap:vpp", StringComparison.Ordinal)).Hint);
    }

    /// <summary>A working OpenCL runtime passes the check and adds no finding.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task WorkingRuntimeAddsNoFinding()
    {
        var report = await RunAsync(openclStarts: true);

        Assert.DoesNotContain(report.Findings, f => f.Code == "opencl-unavailable");
        Assert.Equal(ProbeOutcome.Pass, Assert.Single(report.Probes, p => p.Stage == ProbeStage.Tier).Outcome);
    }

    /// <summary>An encoder that drops low-power mode fails the low-power cell, not the plain one.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task DroppedLowPowerIsUnsupported()
    {
        var report = await RunAsync(openclStarts: true, lowPowerDropped: "hevc");

        var vaapi = Assert.Single(report.Backends);
        Assert.Equal(ProbeOutcome.CodecUnsupported, vaapi.Encode["hevc_lowpower"]);
        Assert.Equal(ProbeOutcome.Pass, vaapi.Encode["hevc"]);
        Assert.Equal(ProbeOutcome.Pass, vaapi.Encode["h264_lowpower"]);
        Assert.Contains(report.Findings, f => f.Code == "lowpower-unavailable-hevc");
    }

    /// <summary>Low power dropped for the bitrate gets the firmware remedy when HuC is known not to be requested, and the dropped text otherwise; -1 on this Gen 9 GPU means the kernel's default, off.</summary>
    /// <param name="enableGuc">The i915 enable_guc value, or "unreadable".</param>
    /// <param name="expectedCode">The H.264 low-power finding.</param>
    /// <param name="textKey">The catalog text the finding contains.</param>
    /// <param name="value">The value the text names, or null.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("2", "lowpower-dropped-h264", "lowPowerDropped", null)]
    [InlineData("3", "lowpower-dropped-h264", "lowPowerDropped", null)]
    [InlineData("unreadable", "lowpower-dropped-h264", "lowPowerDropped", null)]
    [InlineData("0", "lowpower-unavailable-h264", "lowPowerEnableGuc", "0")]
    [InlineData("-1", "lowpower-unavailable-h264", "lowPowerEnableGuc", "-1")]
    public async Task DroppedLowPowerBlamesFirmwareWhenHucIsOff(string enableGuc, string expectedCode, string textKey, string? value)
    {
        var expectedText = value is null ? Catalog.Text(textKey) : Catalog.Text(textKey, ("value", value));
        var report = await RunAsync(openclStarts: true, lowPowerDropped: "h264", enableGuc: enableGuc);

        var finding = Assert.Single(report.Findings, f => f.Code.StartsWith("lowpower-", StringComparison.Ordinal) && f.Code.EndsWith("-h264", StringComparison.Ordinal));
        Assert.Equal(expectedCode, finding.Code);
        Assert.Contains(expectedText, finding.Message, StringComparison.Ordinal);
    }

    /// <summary>Progress counts the test clips, then the tests, never going back, and ends at its total.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ProgressCountsClipsAndTests()
    {
        var seen = new CollectingProgress<ProbeProgress>();
        await RunAsync(openclStarts: true, progress: seen);

        var counted = seen.Reports.Where(p => p.Total > 0).ToList();
        Assert.True(counted.Count > 2);
        Assert.All(counted.Zip(counted.Skip(1)), pair => Assert.True(pair.Second.Done >= pair.First.Done, $"{pair.First.Step} {pair.First.Done} then {pair.Second.Step} {pair.Second.Done}"));
        Assert.Contains(counted, p => p.Step.StartsWith("Generating", StringComparison.Ordinal) && p.Done > 0);
        Assert.True(counted[^1].Total > FixtureCatalog.All.Count);
    }

    /// <summary>A second probe finds its clips cached, so they aren't counted or generated again: the count is the tests alone.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CachedClipsAreNotCounted()
    {
        var first = new CollectingProgress<ProbeProgress>();
        await RunAsync(openclStarts: true, progress: first);
        var second = new CollectingProgress<ProbeProgress>();
        await RunAsync(openclStarts: true, progress: second);

        var counted = second.Reports.Where(p => p.Total > 0).ToList();
        Assert.DoesNotContain(counted, p => p.Step.StartsWith("Generating ", StringComparison.Ordinal) && p.Step != "Generating test clips");
        Assert.Equal(first.Reports[^1].Total - counted[^1].Total, first.Reports.Count(p => p.Step.StartsWith("Generating ", StringComparison.Ordinal) && p.Step != "Generating test clips"));
    }

    /// <summary>A failed open of a node this user can't access is PermissionDenied with the render-group fix, not NotPresent.</summary>
    /// <param name="denied">Whether the OS refuses to open the node.</param>
    /// <param name="verdict">The expected verdict.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true, BackendVerdict.PermissionDenied)]
    [InlineData(false, BackendVerdict.NotPresent)]
    public async Task DeniedRenderNodeIsPermissionDenied(bool denied, BackendVerdict verdict)
    {
        var report = await RunAsync(openclStarts: true, nodeDenied: denied, openFails: true);

        var vaapi = Assert.Single(report.Backends);
        Assert.Equal(verdict, vaapi.Verdict);
        Assert.Equal(denied, vaapi.Hint == Catalog.Text("permissionDeniedHost"));
    }

    /// <summary>A QSV render node whose VAAPI parent loads a non-Intel driver says so and gets no fix, even in a container; recorded from a Radeon RX 480 beside an Intel UHD 630 (jellyfin-ffmpeg 8.1.3, docker).</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task QsvOnNonIntelGpuSaysSo()
    {
        const string Amd = "[VAAPI @ 0x1] VAAPI driver: Mesa Gallium driver 26.0.8 for AMD Radeon RX 480 Graphics (radeonsi, polaris10, ACO, DRM 3.64, 6.18.9).\n"
            + "[QSV @ 0x2] Error setting child device handle: -17\nDevice creation failed: -1313558101.\n";
        _runner.Probe = invocation => EngineRunner.Exited(1, null, invocation.Arguments.Contains("renderD129", StringComparison.Ordinal) ? Amd : IntelDriver);
        var host = new FakeHostPlatform(HostOs.Linux) { OsDescription = "Linux 6.18.9" };
        host.Files["/.dockerenv"] = string.Empty;
        host.Files[Node] = string.Empty;
        host.Files["/dev/dri/renderD129"] = string.Empty;
        var options = new EngineOptions(
            new FfmpegLocation("/usr/lib/jellyfin-ffmpeg/ffmpeg", FfmpegSource.CommandLine),
            StopStage.Devices,
            new HashSet<HwType> { HwType.qsv },
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            Path.Combine(_root, "fixtures"),
            Path.Combine(_root, "reports"),
            Refresh: true);

        using var engine = new ProbeEngine(_runner, new FakeArgumentSource(), host, TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };
        var report = await engine.RunAsync(options, TestContext.Current.CancellationToken);

        var amd = Assert.Single(report.Backends, b => b.Device == "/dev/dri/renderD129");
        Assert.Equal((BackendVerdict.NotPresent, Catalog.Text("notIntelGpu")), (amd.Verdict, amd.Hint));
        Assert.Null(amd.Fix);
        Assert.NotEqual(Catalog.Text("notIntelGpu"), Assert.Single(report.Backends, b => b.Device == Node).Hint);
    }

    /// <summary>A render node libva finds no driver for says so and gets no fix, even in a container; recorded on an RK3588S, whose Mali GPU has no VA-API driver.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task NodeWithoutVaapiDriverSaysSo()
    {
        var noDriver = CorpusFile.Load("stderr/jellyfin-linux-vaapi-rockchip-no-driver.txt");
        _runner.Probe = _ => EngineRunner.Exited(251, null, noDriver);
        var host = new FakeHostPlatform(HostOs.Linux) { OsDescription = "Linux 6.1.115" };
        host.Files["/.dockerenv"] = string.Empty;
        host.Files[Node] = string.Empty;
        var options = new EngineOptions(
            new FfmpegLocation("/usr/lib/jellyfin-ffmpeg/ffmpeg", FfmpegSource.CommandLine),
            StopStage.Devices,
            new HashSet<HwType> { HwType.vaapi },
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            Path.Combine(_root, "fixtures"),
            Path.Combine(_root, "reports"),
            Refresh: true);

        using var engine = new ProbeEngine(_runner, new FakeArgumentSource(), host, TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };
        var report = await engine.RunAsync(options, TestContext.Current.CancellationToken);

        var vaapi = Assert.Single(report.Backends);
        Assert.Equal((BackendVerdict.NotPresent, Catalog.Text("noVaapiDriver", ("driver", "rockchip_drv_video.so"))), (vaapi.Verdict, vaapi.Hint));
        Assert.Null(vaapi.Fix);
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Runs the engine against one Intel render node; tone-map probes fail, everything else passes.</summary>
    /// <param name="openclStarts">Whether deriving OpenCL from the VAAPI device succeeds.</param>
    /// <param name="lowPowerDropped">An output codec whose encoder drops low-power mode, or null.</param>
    /// <param name="nodeDenied">Whether the OS refuses to open the render node.</param>
    /// <param name="openFails">Whether the device open fails.</param>
    /// <param name="progress">Receives the probe's progress, or null.</param>
    /// <param name="enableGuc">The i915 enable_guc value, "unreadable" for a parameter only root can read, or null when i915 isn't loaded.</param>
    /// <param name="openclAborts">Whether a failing OpenCL derive aborts (glibc's "free(): invalid pointer", exit 134) instead of logging the failure.</param>
    /// <returns>The report.</returns>
    private async Task<CapabilityReport> RunAsync(bool openclStarts, string? lowPowerDropped = null, bool nodeDenied = false, bool openFails = false, IProgress<ProbeProgress>? progress = null, string? enableGuc = null, bool openclAborts = false)
    {
        _runner.Probe = invocation => invocation.Arguments switch
        {
            var a when openFails && !a.Contains("-progress", StringComparison.Ordinal) =>
                EngineRunner.Exited(1, null, "[AVHWDeviceContext @ 0x1] Failed to initialise VAAPI connection: -1 (unknown libva error).\nDevice creation failed: -5.\n"),
            var a when lowPowerDropped is not null && a.Contains($"-c:v {lowPowerDropped}_vaapi -low_power 1", StringComparison.Ordinal) =>
                EngineRunner.Exited(0, 10, "[h264 @ 0x3] Format vaapi chosen by get_format().\n[hevc_qsv @ 0x4] Some encoding parameters are not supported under Low power mode, trying to recover with it set to disabled\n"),
            var a when a.Contains("opencl=ocl@va", StringComparison.Ordinal) && !openclStarts && openclAborts =>
                EngineRunner.Exited(134, null, IntelDriver + "free(): invalid pointer\n"),
            var a when a.Contains("opencl=ocl@va", StringComparison.Ordinal) && !openclStarts =>
                EngineRunner.Exited(237, null, IntelDriver + "[OpenCL @ 0x2] Failed to get number of OpenCL platforms: -1001.\nDevice creation failed: -19.\n"),
            var a when !a.Contains("-progress", StringComparison.Ordinal) => EngineRunner.Exited(1, null, IntelDriver),
            var a when a.Contains("scale_vt=color_transfer", StringComparison.Ordinal) => EngineRunner.Exited(1, 0, "Error reinitializing filters!\n"),
            _ => EngineRunner.Exited(0, 10, "[h264 @ 0x3] Format vaapi chosen by get_format().\n"),
        };

        var host = new FakeHostPlatform(HostOs.Linux) { OsDescription = "Linux 6.8.0" };
        host.Files[Node] = string.Empty;
        host.Files["/sys/class/drm/renderD128/device/vendor"] = "0x8086\n";
        host.Files["/sys/class/drm/renderD128/device/device"] = "0x5a85\n";
        if (nodeDenied)
        {
            host.DeniedFiles.Add(Node);
        }

        if (enableGuc is not null)
        {
            host.Files[LowPowerAdvice.EnableGucPath] = enableGuc == "unreadable" ? null : enableGuc + "\n";
        }

        var options = new EngineOptions(
            new FfmpegLocation("/usr/lib/jellyfin-ffmpeg/ffmpeg", FfmpegSource.CommandLine),
            StopStage.Matrix,
            new HashSet<HwType> { HwType.vaapi },
            Node,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            Path.Combine(_root, "fixtures"),
            Path.Combine(_root, "reports"),
            Refresh: true);

        using var engine = new ProbeEngine(_runner, new FakeArgumentSource(), host, TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline, Progress = progress };
        return await engine.RunAsync(options, TestContext.Current.CancellationToken);
    }
}
