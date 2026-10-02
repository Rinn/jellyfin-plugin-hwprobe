using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Storage;
using Jellyfin.Plugin.HwProbe.Core.Verdict;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Runs build enumeration, device probes and the codec matrix, pruning as it goes, and assembles the report.</summary>
public sealed class ProbeEngine : IDisposable
{
    private static readonly HwType[] _unvalidated = [HwType.amf, HwType.rkmpp];

    private readonly IFfmpegRunner _runner;
    private readonly IArgumentSourceFactory _arguments;
    private readonly IHostPlatform _platform;
    private readonly TimeProvider _time;
    private readonly EnvironmentRules _environment;
    private readonly SerialProbeGate _gate;

    /// <summary>Initializes a new instance of the <see cref="ProbeEngine"/> class.</summary>
    /// <param name="runner">Launches ffmpeg.</param>
    /// <param name="arguments">Generates probe arguments per device.</param>
    /// <param name="platform">Host access for device enumeration and host info.</param>
    /// <param name="time">Clock for the report timestamp.</param>
    /// <param name="environment">How to treat the process environment EncodingHelper writes to.</param>
    public ProbeEngine(IFfmpegRunner runner, IArgumentSourceFactory arguments, IHostPlatform platform, TimeProvider time, EnvironmentRules environment)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(environment);

        _environment = environment;

        // Inside the server, restoring would undo variables its own transcodes rely on.
        _gate = new SerialProbeGate(environment.RestoreAfterGeneration ? EncodingHelperEnvironment.Variables : []);

        _runner = runner;
        _arguments = arguments;
        _platform = platform;
        _time = time;
    }

    /// <summary>Gets the downloader for fixtures that can't be generated, such as the VC-1 sample.</summary>
    public IFixtureDownloader FixtureDownloader { get; init; } = new HttpFixtureDownloader();

    /// <summary>Probes the host.</summary>
    /// <param name="options">What to probe.</param>
    /// <param name="cancellationToken">Cancels the run; in-flight ffmpeg trees are killed.</param>
    /// <returns>The report.</returns>
    /// <exception cref="FfmpegUnusableException">ffmpeg is too old, Libav, or produced no version.</exception>
    public async Task<CapabilityReport> RunAsync(EngineOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ffmpeg = options.Ffmpeg.Path;
        var caps = await new FfmpegCapabilityProbe(_runner, options.ProbeTimeout).ProbeAsync(ffmpeg, cancellationToken);
        if (caps.Validation != FfmpegValidation.Valid)
        {
            throw new FfmpegUnusableException($"{ffmpeg}: {caps.Validation}.");
        }

        var host = new HostInfoReader(_platform).Read();
        var devices = new DeviceEnumerator(_platform).Enumerate();
        var run = new Run(options, caps, host, devices);

        if (options.StopAfter >= StopStage.Devices)
        {
            await OpenDevicesAsync(run, cancellationToken);
        }

        var fingerprint = ComputeFingerprint(run, ToolBuild(_arguments));
        var store = new ReportStore(options.ReportCacheDirectory);
        if (options.StopAfter == StopStage.Matrix && !options.Refresh)
        {
            var cached = await store.LoadCachedAsync(fingerprint, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        if (run.Opened.Count > 0)
        {
            var fixtures = await new FixtureBuilder(_runner, ffmpeg, options.FixturesDirectory, options.FixtureTimeout, FixtureDownloader)
                .BuildAsync(Fingerprint.Compute(new FingerprintInputs(ffmpeg, caps.VersionLine, null, null, null, null, null, null)), caps.Encoders, cancellationToken);
            run.Fixtures = fixtures.ToDictionary(f => f.Spec.FileName, StringComparer.Ordinal);

            foreach (var (candidate, open) in run.Opened)
            {
                await ProbeBackendAsync(run, candidate, open, cancellationToken);
            }
        }

        var report = new CapabilityReport(
            CapabilityReport.CurrentSchemaVersion,
            _time.GetUtcNow(),
            fingerprint,
            new FfmpegSummary(ffmpeg, options.Ffmpeg.Source.ToString(), caps.Version?.ToString() ?? "unknown", caps.IsJellyfinBuild),
            new HostSummary(OsName(host.Os), host.Kernel, host.Container)
            {
                GpuVendors = host.Os == HostOs.Windows ? D3d11Adapter.Vendors(run.Adapters) : devices.GpuVendors,
                Architecture = host.Architecture,
            },
            new StageASummary([.. caps.Hwaccels.Order(StringComparer.Ordinal)], caps.BuildStatus, caps.FilterOptions),
            [.. run.Backends
                .Select(b => b.Verdict == BackendVerdict.Viable ? b : b with { Fix = Hints.FixFor(b.Verdict, b.Type, host.Os, host.Container is not null) })
                .OrderBy(b => b.Type).ThenBy(b => b.Device, StringComparer.Ordinal)],
            run.Findings,
            run.Probes)
        {
            HwProbeVersion = CapabilityReport.CurrentHwProbeVersion,
        };

        if (options.StopAfter == StopStage.Matrix)
        {
            await store.SaveCachedAsync(report, cancellationToken);
        }

        return report;
    }

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

    /// <summary>Selects the candidates this run probes.</summary>
    /// <param name="run">Run state.</param>
    /// <returns>Candidates whose backend is built and matches the filters.</returns>
    private static IEnumerable<DeviceCandidate> SelectCandidates(Run run) =>
        run.Devices.Candidates
            .Where(c => run.Options.Types.Count == 0 || run.Options.Types.Contains(c.Type))
            .Where(c => run.Options.Device is null || c.Device == run.Options.Device || c.Device.Length == 0)
            .Where(c => run.Caps.BuildStatus.GetValueOrDefault(c.Type) == BuildStatus.Selectable)

            // EncodingHelper hard-codes CUDA device 0 (v12.1, L1162); other indices are unusable by Jellyfin.
            .Where(c => c.Type != HwType.nvenc || c.Device == "0")

            // Jellyfin picks the adapter by vendor: QSV any Intel one (vendor=0x8086 unless QsvDevice names an index, L964-970),
            // AMF always the first AMD one (vendor=0x1002, L1180; there's no AMF device setting).
            .Where(c => run.Adapters.Count == 0 || c.Type is not (HwType.qsv or HwType.amf) || IsAdapterJellyfinUses(run, c));

    /// <summary>Reports whether Jellyfin could use a Windows adapter index for QSV or AMF.</summary>
    /// <param name="run">The run, for its adapter list.</param>
    /// <param name="candidate">A QSV or AMF candidate.</param>
    /// <returns>True for an Intel adapter (QSV), or the first AMD adapter (AMF).</returns>
    private static bool IsAdapterJellyfinUses(Run run, DeviceCandidate candidate) =>
        candidate.Type == HwType.qsv
            ? AdapterVendor(run, candidate.Device) == "0x8086"
            : candidate.Device == run.Adapters.FindIndex(a => a.Vendor == "0x1002").ToString(CultureInfo.InvariantCulture);

    /// <summary>Returns the vendor of a Windows adapter index.</summary>
    /// <param name="run">The run, for its adapter list.</param>
    /// <param name="device">The adapter index.</param>
    /// <returns>The vendor, or null for an index past the list.</returns>
    private static string? AdapterVendor(Run run, string device) =>
        int.TryParse(device, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < run.Adapters.Count ? run.Adapters[index].Vendor : null;

    /// <summary>Resolves the filter-pipeline tier for a viable device.</summary>
    /// <param name="run">Run state.</param>
    /// <param name="type">The backend.</param>
    /// <param name="driver">The VAAPI driver from the device open.</param>
    /// <returns>The tier, or <see cref="PipelineTier.Unknown"/> outside VAAPI/QSV.</returns>
    private static PipelineTier ResolveTier(Run run, HwType type, VaapiDriver driver)
    {
        // Vulkan DRM interop is not probed; upstream checks it with a separate device init.
        var caps = run.Caps;
        return TierResolver.Resolve(new TierGates(
            type,
            run.Host.Os,
            HasHardwareCodec: true,
            InputIsMpeg4: false,
            caps.SupportsHwaccel("vaapi"),
            caps.SupportsHwaccel("qsv"),
            caps.SupportsHwaccel("d3d11va"),
            BuildGates.VaapiFull(caps.SupportsHwaccel, caps.SupportsFilter, caps.SupportsFilterWithOption),
            BuildGates.OpenclFull(caps.SupportsHwaccel, caps.SupportsFilter, caps.SupportsFilterWithOption),
            BuildGates.VulkanFull(caps.SupportsHwaccel, caps.SupportsFilter, caps.SupportsFilterWithOption),
            caps.SupportsFilter("alphasrc"),
            driver,
            VulkanDrmInterop: false,
            KernelVersion(run.Host.Kernel)));
    }

    /// <summary>Parses the leading <c>major.minor[.patch]</c> of a kernel release.</summary>
    /// <param name="kernel">The release string, e.g. <c>6.8.0-45-generic</c>.</param>
    /// <returns>The version, or 0.0 when unparseable.</returns>
    private static Version KernelVersion(string kernel)
    {
        var numeric = new string([.. kernel.TakeWhile(ch => char.IsAsciiDigit(ch) || ch == '.')]).TrimEnd('.');
        return Version.TryParse(numeric.Contains('.', StringComparison.Ordinal) ? numeric : numeric + ".0", out var version) ? version : new Version(0, 0);
    }

    /// <summary>Identifies the code that judges and generates probes.</summary>
    /// <param name="arguments">The argument source factory, whose assembly generates the arguments.</param>
    /// <returns>Module version IDs, which change whenever a deterministic build's code changes.</returns>
    private static string ToolBuild(IArgumentSourceFactory arguments) =>
        $"{typeof(ProbeEngine).Assembly.ManifestModule.ModuleVersionId}:{arguments.GetType().Assembly.ManifestModule.ModuleVersionId}";

    /// <summary>Computes the fingerprint from build enumeration and device-open by-products.</summary>
    /// <param name="run">Run state.</param>
    /// <param name="toolBuild">The hwprobe build identity.</param>
    /// <returns>The fingerprint.</returns>
    private static string ComputeFingerprint(Run run, string toolBuild)
    {
        var identities = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var node in run.Devices.RenderNodes)
        {
            identities[node.Node] = $"{node.Vendor}:{node.Device}:{run.DriverLines.GetValueOrDefault(node.Node) ?? DeviceEnumerator.Unknown}";
        }

        // Installing a Windows GPU driver changes its adapter IDs, which must invalidate a cached report.
        for (var index = 0; index < run.Adapters.Count; index++)
        {
            identities[$"dx11:{index.ToString(CultureInfo.InvariantCulture)}"] = $"{run.Adapters[index].Vendor}:{run.Adapters[index].Device}";
        }

        return Fingerprint.Compute(new FingerprintInputs(
            run.Options.Ffmpeg.Path,
            run.Caps.VersionLine,
            [.. run.Caps.Hwaccels],
            [.. run.Devices.Candidates.Select(c => $"{c.Type}:{c.Device}").Distinct(StringComparer.Ordinal)],
            identities,
            run.Host.Os == HostOs.MacOS ? run.Host.Kernel : null,
            run.Host.Os.ToString(),
            run.Host.Kernel)
        {
            ToolBuild = toolBuild,
        });
    }

    /// <summary>Report key for a hardware tone-map: the filter family the tier implies.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="tier">The resolved tier.</param>
    /// <returns><c>opencl</c>, <c>vulkan</c> or <c>cuda</c> where the tier or backend implies one; otherwise the backend name.</returns>
    private static string TonemapKey(HwType type, PipelineTier tier) => (type, tier) switch
    {
        (HwType.vaapi or HwType.qsv, PipelineTier.FullOpencl) => "opencl",
        (HwType.vaapi, PipelineTier.FullVulkan) => "vulkan",
        (HwType.nvenc, _) => "cuda",
        _ => type.ToString(),
    };

    /// <summary>Appends a probe to the run and returns it.</summary>
    /// <param name="run">Run state.</param>
    /// <param name="probe">The probe.</param>
    /// <returns>The same probe.</returns>
    private static ProbeResult Add(Run run, ProbeResult probe)
    {
        run.Probes.Add(probe);
        return probe;
    }

    /// <summary>Returns the Intel low-power encoders a candidate's render node has, from its PCI ID.</summary>
    /// <param name="run">The run, for the render nodes' PCI IDs.</param>
    /// <param name="candidate">The device.</param>
    /// <returns>The support; Unknown when the node isn't a recognised Intel GPU.</returns>
    private static LowPowerSupport IntelLowPower(Run run, DeviceCandidate candidate) =>
        run.Devices.RenderNodes.FirstOrDefault(n => n.Node == candidate.Device) is { } node ? IntelGraphics.LowPower(node.Vendor, node.Device) : LowPowerSupport.Unknown;

    /// <summary>Creates a probe record.</summary>
    /// <param name="candidate">The device.</param>
    /// <param name="cell">The cell, or null for a device open.</param>
    /// <param name="stage">The stage.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="result">The run, or null when nothing launched.</param>
    /// <param name="hint">Remedy or skip reason.</param>
    /// <param name="commandLine">The arguments launched, or null.</param>
    /// <returns>The record.</returns>
    private static ProbeResult Record(DeviceCandidate candidate, MatrixCell? cell, ProbeStage stage, ProbeOutcome outcome, FfmpegRunResult? result, string hint, string? commandLine)
    {
        var group = cell is null || cell.Group == MatrixGroup.Smoke ? null : cell.Group.ToString();
        var id = string.Join(':', new[] { candidate.Type.ToString(), candidate.Device, stage.ToString(), group, cell?.Key }.Where(p => !string.IsNullOrEmpty(p)));
        return new ProbeResult(
            id,
            candidate.Type,
            candidate.Device,
            cell?.Cell.InputCodec,
            stage,
            outcome,
            null,
            result?.Duration ?? TimeSpan.Zero,
            outcome == ProbeOutcome.Pass ? string.Empty : hint,
            result is null ? string.Empty : VerdictEvaluator.StderrTail(result.Stderr))
        {
            CommandLine = commandLine,
        };
    }

    /// <summary>A row for a device that never reached the codec matrix.</summary>
    /// <param name="candidate">The device.</param>
    /// <param name="verdict">Its verdict.</param>
    /// <param name="hint">Remedy text.</param>
    /// <returns>The row.</returns>
    private static BackendReport EmptyRow(DeviceCandidate candidate, BackendVerdict verdict, string hint) =>
        new(candidate.Type, candidate.Device, verdict, PipelineTier.Unknown, new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), hint);

    /// <summary>Formats a device for a message prefix.</summary>
    /// <param name="device">The device, possibly empty.</param>
    /// <returns><c> device: </c>, or <c>: </c> when there is none.</returns>
    private static string DevicePrefix(string device) => device.Length == 0 ? ": " : $" {device}: ";

    /// <summary>Lowercase OS name for the report.</summary>
    /// <param name="os">The host OS.</param>
    /// <returns>e.g. <c>linux</c>.</returns>
    private static string OsName(HostOs os) => os switch
    {
        HostOs.Linux => "linux",
        HostOs.Windows => "windows",
        HostOs.MacOS => "macos",
        _ => "other",
    };

    /// <summary>Lists the Direct3D adapters by opening each index, stopping where ffmpeg names none.</summary>
    /// <param name="run">The run to fill.</param>
    /// <param name="cancellationToken">Cancels the opens.</param>
    /// <returns>A task that completes when the adapters are listed.</returns>
    private async Task ListAdaptersAsync(Run run, CancellationToken cancellationToken)
    {
        // One past the candidate indices, so the software adapter listed last doesn't hide a fourth GPU's vendor.
        for (var index = 0; index <= DeviceEnumerator.AdapterCount; index++)
        {
            var arguments = $"-v verbose -hide_banner -init_hw_device d3d11va=dx11:{index.ToString(CultureInfo.InvariantCulture)}";
            var invocation = new FfmpegInvocation(run.Options.Ffmpeg.Path, arguments, _environment.Baseline, run.Options.ProbeTimeout);
            var result = await _gate.RunAsync(ct => _runner.RunAsync(invocation, ct), cancellationToken);
            if (D3d11Adapter.Parse(result.Stderr) is not { } adapter)
            {
                return;
            }

            run.Adapters.Add(adapter);
        }
    }

    /// <summary>Reads the i915 driver's enable_guc parameter.</summary>
    /// <returns>The value, or null when the i915 driver isn't loaded.</returns>
    private string? EnableGuc() => _platform.TryReadText(LowPowerAdvice.EnableGucPath)?.Trim();

    /// <summary>Opens every selected device.</summary>
    /// <param name="run">Run state.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>A task that completes when every device has been opened or rejected.</returns>
    private async Task OpenDevicesAsync(Run run, CancellationToken cancellationToken)
    {
        var inContainer = run.Host.Container is not null;
        if (run.Host.Os == HostOs.Windows && run.Caps.SupportsHwaccel("d3d11va"))
        {
            await ListAdaptersAsync(run, cancellationToken);
        }

        var selected = SelectCandidates(run).ToList();

        // A built backend with no device to try still gets a row: a missing /dev/dri in a container is
        // the most common misconfiguration, and silence would hide it.
        if (run.Options.Device is null)
        {
            var deviceless = Enum.GetValues<HwType>()
                .Where(t => t != HwType.none && (run.Options.Types.Count == 0 || run.Options.Types.Contains(t)))
                .Where(t => run.Caps.BuildStatus.GetValueOrDefault(t) == BuildStatus.Selectable && !selected.Any(c => c.Type == t));
            foreach (var type in deviceless)
            {
                var denied = run.Devices.RenderNodeAccess == DirectoryAccess.Denied && type is HwType.vaapi or HwType.qsv;
                var outcome = denied ? ProbeOutcome.PermissionDenied : ProbeOutcome.DeviceUnavailable;
                var hint = run.Adapters.Count > 0 && type is HwType.qsv or HwType.amf ? Hints.NoVendorAdapter(type, null) : Hints.For(outcome, type, run.Host.Os, inContainer);
                run.Backends.Add(EmptyRow(new DeviceCandidate(type, string.Empty), denied ? BackendVerdict.PermissionDenied : BackendVerdict.NotPresent, hint));
            }
        }
        else if (run.Adapters.Count > 0)
        {
            // A requested adapter that isn't the backend's vendor would otherwise give no row at all.
            foreach (var type in new[] { HwType.qsv, HwType.amf }.Where(t => (run.Options.Types.Count == 0 || run.Options.Types.Contains(t))
                && run.Caps.BuildStatus.GetValueOrDefault(t) == BuildStatus.Selectable && !selected.Any(c => c.Type == t)))
            {
                run.Backends.Add(EmptyRow(new DeviceCandidate(type, run.Options.Device), BackendVerdict.NotPresent, Hints.NoVendorAdapter(type, run.Options.Device)));
            }
        }

        // Adapter and CUDA indices are contiguous: once one fails to open, higher ones don't exist.
        var exhausted = new HashSet<HwType>();
        foreach (var candidate in selected)
        {
            if (exhausted.Contains(candidate.Type))
            {
                continue;
            }

            var arguments = DeviceOpenProbe.Arguments(candidate.Type, candidate.Device, run.Host.Os);
            if (arguments is null)
            {
                run.Opened.Add((candidate, new DeviceOpenResult(ProbeOutcome.Pass, VaapiDriver.Other, null)));
                continue;
            }

            var invocation = new FfmpegInvocation(run.Options.Ffmpeg.Path, arguments, _environment.Baseline, run.Options.ProbeTimeout);
            var result = await _gate.RunAsync(ct => _runner.RunAsync(invocation, ct), cancellationToken);
            var open = DeviceOpenProbe.Evaluate(candidate.Type, result);

            if (open.DriverDescription is not null)
            {
                run.DriverLines[candidate.Device] = open.DriverDescription;
            }

            var hint = Hints.For(open.Outcome, candidate.Type, run.Host.Os, inContainer);
            run.Probes.Add(Record(candidate, null, ProbeStage.DeviceOpen, open.Outcome, result, hint, arguments));
            if (open.Outcome == ProbeOutcome.Pass)
            {
                run.Opened.Add((candidate, open));
                continue;
            }

            // Without an adapter listing, a failed index means the rest don't exist; with one, each is a real adapter.
            if (run.Adapters.Count == 0 && int.TryParse(candidate.Device, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                exhausted.Add(candidate.Type);
            }

            // ffmpeg's VAAPI open drops errno (hwcontext_vaapi.c, vaapi_device_create), so a render node this
            // user can't open fails like a missing one; ask the OS directly.
            var renderNode = candidate.Type is HwType.vaapi or HwType.qsv;
            var denied = open.Outcome == ProbeOutcome.PermissionDenied
                || (renderNode && run.Devices.RenderNodeAccess == DirectoryAccess.Denied)
                || (renderNode && open.Outcome == ProbeOutcome.DeviceUnavailable && _platform.IsAccessDenied(candidate.Device));
            var verdict = denied ? BackendVerdict.PermissionDenied : BackendVerdict.NotPresent;
            run.Backends.Add(EmptyRow(candidate, verdict, denied ? Hints.For(ProbeOutcome.PermissionDenied, candidate.Type, run.Host.Os, inContainer) : hint));
        }
    }

    /// <summary>Smoke probe, tier and codec matrix for one opened device.</summary>
    /// <param name="run">Run state.</param>
    /// <param name="candidate">The device.</param>
    /// <param name="open">Its device-open result.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>A task that completes when the device's row is added.</returns>
    private async Task ProbeBackendAsync(Run run, DeviceCandidate candidate, DeviceOpenResult open, CancellationToken cancellationToken)
    {
        var source = _arguments.Create(run.Caps, new DeviceTraits(open.Driver));
        var inContainer = run.Host.Container is not null;

        // v4l2m2m's smoke probe is its single encode cell; it has no hardware decode to test.
        var smokeCell = candidate.Type == HwType.v4l2m2m ? MatrixCatalog.For(HwType.v4l2m2m)[0] : MatrixCatalog.Smoke;
        var smoke = await RunCellAsync(run, candidate, source, smokeCell, ProbeStage.Smoke, cancellationToken);
        if (smoke.Outcome != ProbeOutcome.Pass)
        {
            // Some backends (rkmpp, v4l2m2m) have no device open that touches hardware, so a missing device first shows here.
            var verdict = smoke.Outcome switch
            {
                ProbeOutcome.Untested => BackendVerdict.Untested,
                ProbeOutcome.DeviceUnavailable => BackendVerdict.NotPresent,
                ProbeOutcome.PermissionDenied => BackendVerdict.PermissionDenied,
                _ => BackendVerdict.DevicePresentPipelineBroken,
            };
            run.Backends.Add(EmptyRow(candidate, verdict, Hints.For(smoke.Outcome, candidate.Type, run.Host.Os, inContainer)));
            return;
        }

        var tier = candidate.Type switch
        {
            HwType.videotoolbox => VideoToolboxTier.Resolve(run.Caps.SupportsHwaccel, run.Caps.SupportsFilter),
            HwType.nvenc => CudaTier.Resolve(run.Caps.SupportsHwaccel, run.Caps.SupportsFilter, run.Caps.SupportsFilterWithOption),
            _ => ResolveTier(run, candidate.Type, open.Driver),
        };
        if (tier == PipelineTier.LegacyCopyBack)
        {
            // VideoToolbox and CUDA only drop to copy-back for a build missing filters; Intel and AMD for OpenCL.
            var missing = candidate.Type switch
            {
                HwType.videotoolbox => VideoToolboxTier.MissingFilters(run.Caps.SupportsHwaccel, run.Caps.SupportsFilter),
                HwType.nvenc => CudaTier.Missing(run.Caps.SupportsHwaccel, run.Caps.SupportsFilter, run.Caps.SupportsFilterWithOption),
                _ => null,
            };
            var remedy = missing is null
                ? Hints.LegacyCopyBack
                : $"Filters run in software (copy-back). This ffmpeg lacks {string.Join(", ", missing)}; use jellyfin-ffmpeg for the {(candidate.Type == HwType.nvenc ? "CUDA" : "Metal")} pipeline.";
            run.Findings.Add(new Finding(FindingSeverity.Warn, "legacy-copyback", $"{candidate.Type}{DevicePrefix(candidate.Device)}{remedy}")
            {
                Fix = missing is null ? Hints.OpenclFix(inContainer) : new Fix("Use jellyfin-ffmpeg", null),
            });
        }

        if (tier == PipelineTier.FullOpencl && DeviceOpenProbe.OpenclArguments(candidate.Type, candidate.Device, run.Host.Os) is { } openclArguments)
        {
            await CheckOpenclAsync(run, candidate, openclArguments, cancellationToken);
        }

        if (open.Driver == VaapiDriver.Amd)
        {
            run.Findings.Add(new Finding(FindingSeverity.Info, "vulkan-interop-unprobed", $"{candidate.Type}{DevicePrefix(candidate.Device)}Vulkan DRM interop is not probed, so FullVulkan is never reported."));
        }

        if (_unvalidated.Contains(candidate.Type))
        {
            run.Findings.Add(new Finding(FindingSeverity.Info, "unvalidated-backend", $"{candidate.Type}: hwprobe's {candidate.Type} checks have not been validated on real hardware."));
        }

        Dictionary<string, ProbeOutcome> decode = [];
        Dictionary<string, ProbeOutcome> encode = [];
        Dictionary<string, ProbeOutcome> tonemap = [];
        Dictionary<string, ProbeOutcome> deinterlace = [];
        Dictionary<string, ProbeOutcome> subtitles = [];
        var decodedTenBit = false;
        if (run.Options.StopAfter == StopStage.Matrix)
        {
            foreach (var cell in MatrixCatalog.For(candidate.Type))
            {
                // Tone-map needs a 10-bit source the device can decode.
                if (cell.Group == MatrixGroup.Tonemap && !decodedTenBit)
                {
                    continue;
                }

                var result = await RunCellAsync(run, candidate, source, cell, ProbeStage.Matrix, cancellationToken);
                switch (cell.Group)
                {
                    case MatrixGroup.Decode:
                        decode[cell.Key] = result.Outcome;
                        decodedTenBit |= cell.Fixture.BitDepth >= 10 && result.Outcome == ProbeOutcome.Pass;
                        break;
                    case MatrixGroup.Encode:
                        encode[cell.Key] = result.Outcome;
                        break;
                    case MatrixGroup.Tonemap when result.Outcome != ProbeOutcome.Skipped:
                        tonemap[cell.Cell.VppTonemap ? cell.Key : TonemapKey(candidate.Type, tier)] = result.Outcome;
                        break;

                    // Keyed by the hardware family that deinterlaced; CPU deinterlacing is Skipped and left out, like tone-map.
                    case MatrixGroup.Deinterlace when result.Outcome != ProbeOutcome.Skipped:
                        deinterlace[result.Codec is { } family && family != cell.Cell.InputCodec ? family : cell.Key] = result.Outcome;
                        break;
                    case MatrixGroup.Subtitles:
                        subtitles[cell.Key] = result.Outcome;
                        break;
                    default:
                        break;
                }
            }
        }

        if (candidate.Type is HwType.qsv or HwType.vaapi)
        {
            run.Findings.AddRange(LowPowerAdvice.Findings(candidate.Type, candidate.Device, encode, run.Host.Os, inContainer, EnableGuc(), IntelLowPower(run, candidate)));
        }

        var row = new BackendReport(candidate.Type, candidate.Device, BackendVerdict.Viable, tier, decode, encode, tonemap, deinterlace, subtitles, string.Empty);
        run.Backends.Add(row with { Settings = SettingsAdvisor.For(row, new AdviceContext(run.Host.Os, inContainer, run.NoOpencl.Contains(candidate)) { IntelLowPower = IntelLowPower(run, candidate) }) });
    }

    /// <summary>Opens OpenCL on a device that upstream will send through its OpenCL pipeline.</summary>
    /// <param name="run">Run state.</param>
    /// <param name="candidate">The device.</param>
    /// <param name="arguments">The OpenCL derive arguments.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>A task that completes when the probe is recorded.</returns>
    /// <remarks>Upstream picks the OpenCL pipeline from the build alone (EncodingHelper.IsOpenclFullSupported), so a missing runtime doesn't change the tier; it breaks the OpenCL filters.</remarks>
    private async Task CheckOpenclAsync(Run run, DeviceCandidate candidate, string arguments, CancellationToken cancellationToken)
    {
        var invocation = new FfmpegInvocation(run.Options.Ffmpeg.Path, arguments, _environment.Baseline, run.Options.ProbeTimeout);
        var result = await _gate.RunAsync(ct => _runner.RunAsync(invocation, ct), cancellationToken);
        var outcome = DeviceOpenProbe.EvaluateOpencl(result);
        var remedy = Hints.OpenclUnavailable(run.Host.Container is not null);
        run.Probes.Add(Record(candidate, null, ProbeStage.Tier, outcome, result, outcome == ProbeOutcome.Pass ? string.Empty : remedy, arguments));
        if (outcome != ProbeOutcome.Pass)
        {
            run.NoOpencl.Add(candidate);
            run.Findings.Add(new Finding(
                FindingSeverity.Warn,
                "opencl-unavailable",
                $"{candidate.Type}{DevicePrefix(candidate.Device)}OpenCL doesn't start, but Jellyfin still picks its OpenCL pipeline because this ffmpeg was built with OpenCL, so OpenCL tone-mapping fails. {remedy}")
            {
                Fix = Hints.OpenclFix(run.Host.Container is not null),
            });
        }
    }

    /// <summary>Builds, runs and classifies one cell under the probe gate.</summary>
    /// <param name="run">Run state.</param>
    /// <param name="candidate">The device.</param>
    /// <param name="source">Argument source for the device.</param>
    /// <param name="cell">The cell.</param>
    /// <param name="stage">Smoke or Matrix.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The recorded probe.</returns>
    private async Task<ProbeResult> RunCellAsync(Run run, DeviceCandidate candidate, IArgumentSource source, MatrixCell cell, ProbeStage stage, CancellationToken cancellationToken)
    {
        var inContainer = run.Host.Container is not null;

        // A burn-in cell needs its subtitle file's path before arguments can be generated.
        var probeCell = cell.Cell;
        if (cell.SubtitleFixture is { } subtitleSpec)
        {
            var subtitle = run.Fixtures.GetValueOrDefault(subtitleSpec.FileName);
            if (subtitle?.Status != FixtureStatus.Available)
            {
                var missing = subtitle?.Status == FixtureStatus.Untested ? ProbeOutcome.Untested : ProbeOutcome.Skipped;
                return Add(run, Record(candidate, cell, stage, missing, null, subtitle?.Reason ?? $"No {subtitleSpec.FileName} fixture.", null));
            }

            probeCell = probeCell with { SubtitlePath = subtitle.Path };
        }

        var result = await _gate.RunAsync(
            async ct =>
            {
                ProbeArguments args;
                try
                {
                    args = source.Build(candidate.Type, candidate.Device.Length == 0 ? null : candidate.Device, probeCell);
                }
                catch (ArgumentConstructionException ex)
                {
                    return Record(candidate, cell, stage, ProbeOutcome.NotUsed, null, ex.Message, null);
                }
                catch (UnsafeProbeException ex)
                {
                    return Record(candidate, cell, stage, ProbeOutcome.Untested, null, ex.Message, null);
                }
                catch (NotSupportedException ex)
                {
                    return Record(candidate, cell, stage, ProbeOutcome.Untested, null, $"EncodingHelper reached an unmodelled member: {ex.Message}", null);
                }

                if (cell.Cell.HardwareDecode && args.HardwareDecoder is null)
                {
                    return Record(candidate, cell, stage, ProbeOutcome.NotUsed, null, $"Jellyfin would decode {cell.Cell.InputCodec} in software on this build.", null);
                }

                if (cell.Cell.HardwareEncode && !args.HardwareEncoder)
                {
                    return Record(candidate, cell, stage, ProbeOutcome.NotUsed, null, $"No {candidate.Type} encoder for {cell.Cell.OutputCodec} in this build.", null);
                }

                if (cell.Cell.LowPower && !args.LowPowerEncoder)
                {
                    return Record(candidate, cell, stage, ProbeOutcome.Skipped, null, $"Jellyfin doesn't use low-power mode for {args.VideoEncoder} with this driver.", null);
                }

                if (cell.Group == MatrixGroup.Tonemap && !args.HardwareTonemap)
                {
                    return Record(candidate, cell, stage, ProbeOutcome.Skipped, null, $"Jellyfin emits no hardware tone-map for this backend and build (filters:{args.FilterArgs}).", null);
                }

                if (cell.Group == MatrixGroup.Deinterlace && args.HardwareDeinterlacer is null)
                {
                    return Record(candidate, cell, stage, ProbeOutcome.Skipped, null, $"Jellyfin deinterlaces on the CPU for this backend and build (filters:{args.FilterArgs}).", null);
                }

                // Jellyfin falls back to YADIF when the build lacks the BWDIF filter, and VAAPI and QSV ignore the method.
                if (cell.Cell.Bwdif && !args.FilterArgs.Contains("bwdif_", StringComparison.Ordinal))
                {
                    return Record(candidate, cell, stage, ProbeOutcome.Skipped, null, $"Jellyfin doesn't use a hardware BWDIF filter for this backend and build (filters:{args.FilterArgs}).", null);
                }

                // Checked after asking Jellyfin: a codec it won't hardware-decode needs no clip to say so.
                var fixture = run.Fixtures.GetValueOrDefault(cell.Fixture.FileName);
                if (fixture?.Status != FixtureStatus.Available)
                {
                    var missing = fixture?.Status == FixtureStatus.Untested ? ProbeOutcome.Untested : ProbeOutcome.Skipped;
                    return Record(candidate, cell, stage, missing, null, fixture?.Reason ?? $"No {cell.Fixture.FileName} fixture.", null);
                }

                var commandLine = ProbeCommandLine.Build(args, fixture.Path!, MatrixCatalog.Frames);
                var invocation = new FfmpegInvocation(run.Options.Ffmpeg.Path, commandLine, args.Environment, run.Options.ProbeTimeout);
                var ran = await _runner.RunAsync(invocation, ct);
                var outcome = ran.Status == FfmpegRunStatus.LaunchFailed
                    ? ProbeOutcome.DeviceUnavailable
                    : cell.Cell.LowPower && StderrMarkers.LowPowerDisabled.Any(m => ran.Stderr.Contains(m, StringComparison.Ordinal))
                    ? ProbeOutcome.CodecUnsupported
                    : VerdictEvaluator.Evaluate(ran, new ProbeExpectation(MatrixCatalog.Frames, StderrMarkers.HardwareFrames(candidate.Type, args.Hwaccel)));
                var hint = outcome == ProbeOutcome.Pass ? string.Empty
                    : cell.Cell.LowPower ? LowPowerAdvice.Remedy(cell.Cell.OutputCodec, run.Host.Os, inContainer, EnableGuc(), IntelLowPower(run, candidate))
                    : cell.Group == MatrixGroup.Tonemap && !cell.Cell.VppTonemap && run.NoOpencl.Contains(candidate) ? Hints.OpenclUnavailable(inContainer)
                    : Hints.For(outcome, candidate.Type, run.Host.Os, inContainer);
                var recorded = Record(candidate, cell, stage, outcome, ran, hint, commandLine);

                // The deinterlace column is keyed by the hardware filter family that did the work.
                return cell.Group == MatrixGroup.Deinterlace ? recorded with { Codec = args.HardwareDeinterlacer + (cell.Cell.Bwdif ? "_bwdif" : string.Empty) } : recorded;
            },
            cancellationToken);

        return Add(run, result);
    }

    /// <summary>Mutable state for one run.</summary>
    /// <param name="Options">The run options.</param>
    /// <param name="Caps">Build capabilities.</param>
    /// <param name="Host">Host info.</param>
    /// <param name="Devices">Enumerated devices.</param>
    private sealed record Run(EngineOptions Options, FfmpegCapabilities Caps, HostInfo Host, DeviceEnumeration Devices)
    {
        /// <summary>Gets devices that opened.</summary>
        public List<(DeviceCandidate Candidate, DeviceOpenResult Open)> Opened { get; } = [];

        /// <summary>Gets devices on the OpenCL pipeline whose OpenCL runtime doesn't start.</summary>
        public HashSet<DeviceCandidate> NoOpencl { get; } = [];

        /// <summary>Gets VAAPI driver lines by device, for the fingerprint.</summary>
        public Dictionary<string, string> DriverLines { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the Direct3D adapters on Windows, in index order; empty elsewhere or when the build lacks d3d11va.</summary>
        public List<(string Vendor, string Device)> Adapters { get; } = [];

        /// <summary>Gets or sets fixtures by file name.</summary>
        public Dictionary<string, FixtureResult> Fixtures { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Gets every probe in execution order.</summary>
        public List<ProbeResult> Probes { get; } = [];

        /// <summary>Gets one row per probed device.</summary>
        public List<BackendReport> Backends { get; } = [];

        /// <summary>Gets report-level findings.</summary>
        public List<Finding> Findings { get; } = [];
    }
}
