using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Tests.Devices;
using Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Staged probing and pruning in <see cref="ProbeEngine"/>, on a fake macOS host.</summary>
[Trait("Category", "Unit")]
public sealed class ProbeEngineTests : IDisposable
{
    private const string HardwareDecode = "[h264 @ 0x1] Format videotoolbox_vld chosen by get_format().\n";

    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-engine-").FullName;
    private readonly EngineRunner _runner = new();
    private readonly FakeArgumentSource _arguments = new();

    /// <summary>Initializes a new instance of the <see cref="ProbeEngineTests"/> class with every probe passing.</summary>
    public ProbeEngineTests()
    {
        _runner.Probe = i => i.Arguments.StartsWith("-v verbose", StringComparison.Ordinal)
            ? EngineRunner.Exited(1, null, "usage: ffmpeg [options]\n")
            : EngineRunner.Exited(0, 10, HardwareDecode);
    }

    /// <summary>A working device is Viable, gets a tier, and fills the matrix.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task WorkingDeviceIsViableWithMatrix()
    {
        var report = await RunAsync(StopStage.Matrix);

        var backend = Assert.Single(report.Backends);
        Assert.Equal((HwType.videotoolbox, BackendVerdict.Viable, PipelineTier.LegacyCopyBack), (backend.Type, backend.Verdict, backend.Tier));
        Assert.Equal(ProbeOutcome.Pass, backend.Decode["hevc_10bit"]);
        Assert.Equal(ProbeOutcome.Untested, backend.Decode["vc1"]);
        Assert.Equal(ProbeOutcome.Pass, backend.Tonemap["videotoolbox"]);
        Assert.Equal(ProbeOutcome.Pass, backend.Deinterlace["videotoolbox"]);
        Assert.Equal(ProbeOutcome.Pass, backend.Subtitles["text"]);
        var copyBack = Assert.Single(report.Findings, f => f.Code == "legacy-copyback");
        Assert.Contains("lacks overlay_videotoolbox, tonemap_videotoolbox, alphasrc; use jellyfin-ffmpeg for the Metal pipeline", copyBack.Message, StringComparison.Ordinal);
        Assert.Equal(CapabilityReport.CurrentSchemaVersion, report.SchemaVersion);
    }

    /// <summary>NVENC with every CUDA filter Jellyfin needs keeps the job on the GPU; without alphasrc it's copy-back, naming it.</summary>
    /// <param name="dropAlphasrc">Whether the recorded build's filter list loses alphasrc.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NvencTierFollowsCudaFilters(bool dropAlphasrc)
    {
        var runner = new EngineRunner("jellyfin-8.1.3-windows-x64")
        {
            // Adapter 0 opens; the others don't, which ends the enumeration.
            Probe = i => i.Arguments.StartsWith("-v verbose", StringComparison.Ordinal) && !i.Arguments.Contains("cuda=cu:0", StringComparison.Ordinal)
                ? EngineRunner.Exited(1, null, "Device creation failed: -1.\n")
                : EngineRunner.Exited(0, 10, "[h264 @ 0x1] Format cuda chosen by get_format().\n"),
            EditCapabilities = (arguments, stdout) => dropAlphasrc && arguments.Contains("-filters", StringComparison.Ordinal)
                ? string.Join('\n', stdout.Split('\n').Where(l => !l.Contains(" alphasrc ", StringComparison.Ordinal)))
                : stdout,
        };
        using var engine = new ProbeEngine(runner, _arguments, new FakeHostPlatform(HostOs.Windows), TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };

        var report = await engine.RunAsync(Options(StopStage.Matrix, refresh: true) with { Types = new HashSet<HwType> { HwType.nvenc } }, TestContext.Current.CancellationToken);

        var nvenc = Assert.Single(report.Backends, b => b.Type == HwType.nvenc);
        Assert.Equal(dropAlphasrc ? PipelineTier.LegacyCopyBack : PipelineTier.FullCuda, nvenc.Tier);
        var copyBack = report.Findings.SingleOrDefault(f => f.Code == "legacy-copyback");
        Assert.Equal(dropAlphasrc, copyBack is not null);
        if (copyBack is not null)
        {
            Assert.Contains("lacks alphasrc; use jellyfin-ffmpeg for the CUDA pipeline", copyBack.Message, StringComparison.Ordinal);
            Assert.Equal("Use jellyfin-ffmpeg", copyBack.Fix?.Action);
        }
    }

    /// <summary>On Windows the adapters are listed up front: vendors come from that list, and QSV is tried only on Intel adapters.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task WindowsAdaptersAreListedAndMatchedByVendor()
    {
        // As ffmpeg logs them: NVIDIA at 0, Intel at 1, the software adapter at 2 (its create fails), nothing past the end.
        var runner = new EngineRunner("jellyfin-8.1.3-windows-x64")
        {
            Probe = i => i.Arguments.Contains("dx11:0", StringComparison.Ordinal) ? EngineRunner.Exited(1, null, "[D3D11VA @ 1] Using device 10de:2c02 (NVIDIA GeForce RTX 5080).\n")
                : i.Arguments.Contains("dx11:1", StringComparison.Ordinal) ? EngineRunner.Exited(1, null, "[D3D11VA @ 1] Using device 8086:a780 (Intel(R) UHD Graphics 770).\n")
                : i.Arguments.Contains("dx11:2", StringComparison.Ordinal) ? EngineRunner.Exited(1, null, "[D3D11VA @ 1] Using device 1414:008c (Microsoft Basic Render Driver).\n[D3D11VA @ 1] Failed to create Direct3D device (887a0004)\nDevice creation failed: -1313558101.\n")
                : EngineRunner.Exited(1, null, "[D3D11VA @ 1] Selecting d3d11va adapter 3\n"),
        };
        using var engine = new ProbeEngine(runner, _arguments, new FakeHostPlatform(HostOs.Windows), TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };

        var report = await engine.RunAsync(Options(StopStage.Devices, refresh: true) with { Types = new HashSet<HwType> { HwType.qsv, HwType.amf } }, TestContext.Current.CancellationToken);

        Assert.Equal(["0x10de", "0x8086"], report.Host.GpuVendors);
        Assert.Equal(["dx11:1"], runner.Calls.Where(c => c.Contains("qsv=qs@dx11", StringComparison.Ordinal)).Select(c => c.Split("d3d11va=")[1].Split(' ')[0]));

        // The listing stops at index 3, the first with no adapter, below its cap of five.
        Assert.Equal(4, runner.Calls.Count(c => c.Contains("-init_hw_device d3d11va=dx11:", StringComparison.Ordinal) && !c.Contains("qsv", StringComparison.Ordinal)));
        var amf = Assert.Single(report.Backends, b => b.Type == HwType.amf);
        Assert.Equal((BackendVerdict.NotPresent, "No AMD adapter found. Check the AMD graphics driver is installed."), (amf.Verdict, amf.Hint));
    }

    /// <summary>With the adapters listed, a failed Intel adapter doesn't hide the next, and AMF tests only the first AMD adapter.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task WindowsAdaptersAreAllTriedButAmfOnlyItsFirst()
    {
        // Intel at 0 (its QSV open fails), Intel at 1, AMD at 2 and 3, the software adapter at 4.
        string[] adapters = ["8086:4680 (Intel(R) UHD Graphics 770)", "8086:56a0 (Intel(R) Arc(TM) A770)", "1002:744c (AMD Radeon RX 7900 XTX)", "1002:164e (AMD Radeon(TM) Graphics)", "1414:008c (Microsoft Basic Render Driver)"];
        var runner = new EngineRunner("jellyfin-8.1.3-windows-x64")
        {
            Probe = i =>
            {
                var index = Enumerable.Range(0, adapters.Length).FirstOrDefault(n => i.Arguments.Contains($"dx11:{n}", StringComparison.Ordinal), -1);
                var line = index < 0 ? string.Empty : $"[D3D11VA @ 1] Using device {adapters[index]}.\n";
                var failed = index == 0 && i.Arguments.Contains("qsv=qs@dx11", StringComparison.Ordinal);
                return EngineRunner.Exited(1, null, line + (failed ? "Device creation failed: -1.\n" : string.Empty));
            },
        };
        using var engine = new ProbeEngine(runner, _arguments, new FakeHostPlatform(HostOs.Windows), TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };

        await engine.RunAsync(Options(StopStage.Devices, refresh: true) with { Types = new HashSet<HwType> { HwType.qsv, HwType.amf } }, TestContext.Current.CancellationToken);

        Assert.Equal(["dx11:0", "dx11:1"], runner.Calls.Where(c => c.Contains("qsv=qs@dx11", StringComparison.Ordinal)).Select(c => c.Split("d3d11va=")[1].Split(' ')[0]));
        Assert.Equal(2, runner.Calls.Count(c => c.EndsWith("d3d11va=dx11:2", StringComparison.Ordinal)));
        Assert.Equal(1, runner.Calls.Count(c => c.EndsWith("d3d11va=dx11:3", StringComparison.Ordinal)));
    }

    /// <summary>A device that won't open is NotPresent after one probe, with nothing further launched.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FailedOpenPrunesAfterOneProbe()
    {
        _runner.Probe = _ => EngineRunner.Exited(234, null, "Device creation failed: -12.\n");

        var report = await RunAsync(StopStage.Matrix);

        var backend = Assert.Single(report.Backends);
        Assert.Equal(BackendVerdict.NotPresent, backend.Verdict);
        Assert.NotEmpty(backend.Hint);
        Assert.Single(_runner.Calls, c => c.Contains("-init_hw_device", StringComparison.Ordinal));
    }

    /// <summary>A failing smoke probe marks the pipeline broken and skips the codec matrix.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FailedSmokeProbeSkipsMatrix()
    {
        _runner.Probe = i => i.Arguments.StartsWith("-v verbose", StringComparison.Ordinal)
            ? EngineRunner.Exited(1, null, "usage: ffmpeg\n")
            : EngineRunner.Exited(1, 0, "Impossible to convert between the formats\n");

        var report = await RunAsync(StopStage.Matrix);

        Assert.Equal(BackendVerdict.DevicePresentPipelineBroken, Assert.Single(report.Backends).Verdict);
        Assert.DoesNotContain(report.Probes, p => p.Stage == ProbeStage.Matrix);
    }

    /// <summary>A clean exit without hardware frames is a software fallback, so the smoke probe fails.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SoftwareFallbackSmokeProbeIsBroken()
    {
        _runner.Probe = i => i.Arguments.StartsWith("-v verbose", StringComparison.Ordinal)
            ? EngineRunner.Exited(1, null, "usage: ffmpeg\n")
            : EngineRunner.Exited(0, 10, "graph input pixfmt:nv12\n");

        var report = await RunAsync(StopStage.Matrix);

        Assert.Equal(BackendVerdict.DevicePresentPipelineBroken, Assert.Single(report.Backends).Verdict);
        Assert.Equal(ProbeOutcome.SoftwareFallback, Assert.Single(report.Probes, p => p.Stage == ProbeStage.Smoke).Outcome);
    }

    /// <summary>A smoke probe that finds no device means the device is absent, not that the pipeline is broken.</summary>
    /// <param name="stderr">The failure text, as logged by jellyfin-ffmpeg 8.1.2 with no device.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("[h264_v4l2m2m @ 0x1] Could not find a valid device\n")]
    [InlineData("[h264_rkmpp @ 0x1] Failed to init MPP context: -1\n")]
    public async Task SmokeProbeWithoutDeviceIsNotPresent(string stderr)
    {
        _runner.Probe = i => i.Arguments.StartsWith("-v verbose", StringComparison.Ordinal)
            ? EngineRunner.Exited(1, null, "usage: ffmpeg\n")
            : EngineRunner.Exited(234, 0, stderr);

        var report = await RunAsync(StopStage.Matrix);

        Assert.Equal(BackendVerdict.NotPresent, Assert.Single(report.Backends).Verdict);
    }

    /// <summary>A built backend with no device candidates still gets a NotPresent row with a remedy.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task BuiltBackendWithoutDevicesGetsRow()
    {
        var linux = new FakeHostPlatform(HostOs.Linux) { Files = { ["/proc/1/cgroup"] = "0::/docker/abc" } };
        var runner = new ScriptedOnly(new()
        {
            ["-version"] = "ffmpeg version 7.1.4-Jellyfin Copyright (c) 2000-2025\n",
            ["-hwaccels"] = "Hardware acceleration methods:\nvaapi\n",
        });
        using var engine = new ProbeEngine(runner, _arguments, linux, TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };

        var report = await engine.RunAsync(Options(StopStage.Devices, refresh: true), TestContext.Current.CancellationToken);

        var vaapi = Assert.Single(report.Backends, b => b.Type == HwType.vaapi);
        Assert.Equal(BackendVerdict.NotPresent, vaapi.Verdict);
        Assert.Contains("--device", vaapi.Hint, StringComparison.Ordinal);
    }

    /// <summary>The report lists the vendors of PCI display controllers only, with or without a render node.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReportListsGpuVendors()
    {
        var linux = new FakeHostPlatform(HostOs.Linux)
        {
            Files =
            {
                ["/dev/dri/renderD128"] = string.Empty,
                ["/sys/bus/pci/devices/0000:00:02.0"] = string.Empty,
                ["/sys/bus/pci/devices/0000:00:02.0/class"] = "0x030000\n",
                ["/sys/bus/pci/devices/0000:00:02.0/vendor"] = "0x8086\n",
                ["/sys/bus/pci/devices/0000:01:00.0"] = string.Empty,
                ["/sys/bus/pci/devices/0000:01:00.0/class"] = "0x030200\n",
                ["/sys/bus/pci/devices/0000:01:00.0/vendor"] = "0x10de\n",
                ["/sys/bus/pci/devices/0000:00:1f.3"] = string.Empty,
                ["/sys/bus/pci/devices/0000:00:1f.3/class"] = "0x040300\n",
                ["/sys/bus/pci/devices/0000:00:1f.3/vendor"] = "0x1002\n",
            },
        };
        var runner = new ScriptedOnly(new() { ["-version"] = "ffmpeg version 7.1.4-Jellyfin Copyright (c) 2000-2025\n" });
        using var engine = new ProbeEngine(runner, _arguments, linux, TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };

        var report = await engine.RunAsync(Options(StopStage.Devices, refresh: true), TestContext.Current.CancellationToken);

        Assert.Equal(["0x10de", "0x8086"], report.Host.GpuVendors);
    }

    /// <summary>A virtual GPU, like WSL2's, means the PCI list can't say which GPUs exist, so no vendors are reported.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task VirtualGpuReportsNoVendors()
    {
        var linux = new FakeHostPlatform(HostOs.Linux)
        {
            Files =
            {
                ["/sys/bus/pci/devices/c00d:00:00.0"] = string.Empty,
                ["/sys/bus/pci/devices/c00d:00:00.0/class"] = "0x030200\n",
                ["/sys/bus/pci/devices/c00d:00:00.0/vendor"] = "0x1414\n",
            },
        };
        var runner = new ScriptedOnly(new() { ["-version"] = "ffmpeg version 7.1.4-Jellyfin Copyright (c) 2000-2025\n" });
        using var engine = new ProbeEngine(runner, _arguments, linux, TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };

        var report = await engine.RunAsync(Options(StopStage.Devices, refresh: true), TestContext.Current.CancellationToken);

        Assert.Empty(report.Host.GpuVendors);
    }

    /// <summary>On Windows, adapter indices stop at the first that fails to open, giving one row.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AdapterIndicesStopAtFirstFailure()
    {
        var runner = new ScriptedOnly(new()
        {
            ["-version"] = "ffmpeg version 8.1.3-Jellyfin Copyright (c) 2000-2026\n",
            ["-hwaccels"] = "Hardware acceleration methods:\nqsv\nd3d11va\n",
        })
        {
            OtherStderr = "Failed to set value 'd3d11va=dx11:0' for option 'init_hw_device': Unknown error occurred\n",
        };
        using var engine = new ProbeEngine(runner, _arguments, new FakeHostPlatform(HostOs.Windows), TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };

        var report = await engine.RunAsync(Options(StopStage.Devices, refresh: true), TestContext.Current.CancellationToken);

        var qsv = Assert.Single(report.Backends, b => b.Type == HwType.qsv);
        Assert.Equal(("0", BackendVerdict.NotPresent), (qsv.Device, qsv.Verdict));
        Assert.Single(runner.Calls, c => c.Contains("qsv=qs@dx11", StringComparison.Ordinal));
    }

    /// <summary>--stage build launches no device probes; --stage devices launches no matrix probes.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task StopStageLimitsWork()
    {
        var a = await RunAsync(StopStage.Build);
        Assert.Empty(a.Backends);
        Assert.DoesNotContain(_runner.Calls, c => c.Contains("-init_hw_device", StringComparison.Ordinal));

        var b = await RunAsync(StopStage.Devices);
        var backend = Assert.Single(b.Backends);
        Assert.Equal(BackendVerdict.Viable, backend.Verdict);
        Assert.Empty(backend.Decode);
        Assert.DoesNotContain(b.Probes, p => p.Stage == ProbeStage.Matrix);
    }

    /// <summary>A codec upstream can't build args for is NotUsed and is never launched.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task UnconstructibleCellIsNotLaunched()
    {
        _arguments.Unconstructible.Add("av1");

        var report = await RunAsync(StopStage.Matrix);

        Assert.Equal(ProbeOutcome.NotUsed, report.Backends[0].Decode["av1"]);
        Assert.DoesNotContain(_runner.Calls, c => c.Contains("-progress", StringComparison.Ordinal) && c.Contains("av1_8bit", StringComparison.Ordinal));
    }

    /// <summary>CPU deinterlacing is left out of the deinterlace column rather than filed under the input codec.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task CpuDeinterlaceIsLeftOut()
    {
        _arguments.NoHardwareDeinterlace = true;

        var report = await RunAsync(StopStage.Matrix);

        Assert.Empty(report.Backends[0].Deinterlace);
        Assert.All(report.Probes.Where(p => p.ProbeId.Contains("Deinterlace", StringComparison.Ordinal)), p => Assert.Equal(ProbeOutcome.Skipped, p.Outcome));
    }

    /// <summary>A codec Jellyfin won't hardware-decode is NotUsed even with no fixture to test it.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SoftwareDecodedCodecNeedsNoFixture()
    {
        _arguments.SoftwareDecoded.Add("vc1");

        var report = await RunAsync(StopStage.Matrix);

        Assert.Equal(ProbeOutcome.NotUsed, report.Backends[0].Decode["vc1"]);
    }

    /// <summary>Environment overrides from arg generation reach the launched probe.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GeneratedEnvironmentReachesProbe()
    {
        _arguments.Environment["LIBVA_DRIVER_NAME"] = "i965";

        await RunAsync(StopStage.Devices);

        var smoke = Assert.Single(_runner.Invocations, i => i.Arguments.Contains("-progress", StringComparison.Ordinal));
        Assert.Equal("i965", smoke.Environment["LIBVA_DRIVER_NAME"]);
    }

    /// <summary>A second full run is served from the cache; --refresh probes again.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task FullRunsAreCachedByFingerprint()
    {
        var first = await RunAsync(StopStage.Matrix);
        var probesAfterFirst = _runner.Calls.Count(c => c.Contains("-progress", StringComparison.Ordinal));

        var second = await RunAsync(StopStage.Matrix);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(probesAfterFirst, _runner.Calls.Count(c => c.Contains("-progress", StringComparison.Ordinal)));

        await RunAsync(StopStage.Matrix, refresh: true);
        Assert.True(_runner.Calls.Count(c => c.Contains("-progress", StringComparison.Ordinal)) > probesAfterFirst);
    }

    /// <summary>An ffmpeg below the 4.4 minimum is rejected before any device probe.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OldFfmpegIsUnusable()
    {
        var old = new ScriptedOnly(new() { ["-version"] = "ffmpeg version 4.3 Copyright (c) 2000-2020\n" });
        using var engine = new ProbeEngine(old, _arguments, new FakeHostPlatform(HostOs.MacOS), TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };

        await Assert.ThrowsAsync<FfmpegUnusableException>(() => engine.RunAsync(Options(StopStage.Matrix, refresh: true), TestContext.Current.CancellationToken));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Runs the engine against the fake macOS host.</summary>
    /// <param name="stop">The last stage.</param>
    /// <param name="refresh">Ignore the cache.</param>
    /// <returns>The report.</returns>
    private async Task<CapabilityReport> RunAsync(StopStage stop, bool refresh = false)
    {
        using var engine = new ProbeEngine(_runner, _arguments, new FakeHostPlatform(HostOs.MacOS) { OsDescription = "macOS 27.0.1" }, TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };
        return await engine.RunAsync(Options(stop, refresh), TestContext.Current.CancellationToken);
    }

    /// <summary>Builds engine options rooted in the test's temp directory.</summary>
    /// <param name="stop">The last stage.</param>
    /// <param name="refresh">Ignore the cache.</param>
    /// <returns>The options.</returns>
    private EngineOptions Options(StopStage stop, bool refresh) => new(
        new FfmpegLocation("/opt/homebrew/bin/ffmpeg", FfmpegSource.CommandLine),
        stop,
        new HashSet<HwType>(),
        null,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(5),
        Path.Combine(_root, "fixtures"),
        Path.Combine(_root, "reports"),
        refresh);
}
