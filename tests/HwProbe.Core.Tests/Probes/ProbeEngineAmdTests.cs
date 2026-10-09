using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Tests.Devices;
using Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>An AMD VAAPI device, from a Radeon RX 480 beside an Intel UHD 630 (jellyfin-ffmpeg 8.1.3, Docker).</summary>
[Trait("Category", "FakeFfmpeg")]
[Trait("Category", "Platform")]
public sealed class ProbeEngineAmdTests : IDisposable
{
    private const string Node = "/dev/dri/renderD129";
    private const string AmdDriver = "[VAAPI @ 0x1] VAAPI driver: Mesa Gallium driver 26.0.8 for AMD Radeon RX 480 Graphics (radeonsi, polaris10, ACO, DRM 3.64, 6.18.9).\n";

    private readonly string _root = Directory.CreateTempSubdirectory("hwprobe-amd-").FullName;
    private readonly EngineRunner _runner = new("jellyfin-8.1.2-linux-amd64");

    /// <summary>A tone map through OpenCL that aborts is untested on AMD, since Jellyfin takes Vulkan there when interop works, and says why.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OpenclTonemapThatAbortsIsUntested()
    {
        var abort = CorpusFile.Load("stderr/jellyfin-linux-vaapi-amd-tonemap-opencl-abort.txt");
        _runner.Probe = invocation => invocation.Arguments switch
        {
            var a when !a.Contains("-progress", StringComparison.Ordinal) => EngineRunner.Exited(1, null, AmdDriver),
            var a when a.Contains("tonemap_opencl", StringComparison.Ordinal) => EngineRunner.Exited(134, null, abort),
            _ => EngineRunner.Exited(0, 10, "[h264 @ 0x3] Format vaapi chosen by get_format().\n"),
        };
        var host = new FakeHostPlatform(HostOs.Linux) { OsDescription = "Linux 6.18.9" };
        host.Files["/.dockerenv"] = string.Empty;
        host.Files[Node] = string.Empty;
        var arguments = new FakeArgumentSource { TonemapFilters = " -vf \"hwupload=derive_device=opencl,tonemap_opencl=format=nv12\"" };
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

        using var engine = new ProbeEngine(_runner, arguments, host, TimeProvider.System, EnvironmentRules.Standalone()) { FixtureDownloader = ScriptedDownloader.Offline };
        var report = await engine.RunAsync(options, TestContext.Current.CancellationToken);

        var tonemap = Assert.Single(report.Probes, p => p.ProbeId.EndsWith(":Tonemap:hevc_10bit", StringComparison.Ordinal));
        Assert.Equal((ProbeOutcome.Untested, Catalog.Text("amdOpenclTonemap")), (tonemap.Outcome, tonemap.Hint));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
