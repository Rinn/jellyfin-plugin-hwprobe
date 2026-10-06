using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.Settings;
using Jellyfin.Plugin.HwProbe.TestSupport;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>A <see cref="SettingsService"/> over in-memory encoding options and a fixed report.</summary>
internal sealed class SettingsHarness : IDisposable
{
    /// <summary>The ffmpeg path the report and the server agree on.</summary>
    public const string Ffmpeg = "/usr/lib/jellyfin-ffmpeg/ffmpeg";

    /// <summary>The probed render node.</summary>
    public const string Node = "/dev/dri/renderD128";

    private readonly string _directory = Directory.CreateTempSubdirectory("hwprobe-settings-").FullName;

    /// <summary>Initializes a new instance of the <see cref="SettingsHarness"/> class.</summary>
    public SettingsHarness()
    {
        Service = new SettingsService(
            () => new ServerSettings(Copy(Saved), new TrickplayOptions { EnableHwAcceleration = SavedTrickplay.EnableHwAcceleration, EnableHwEncoding = SavedTrickplay.EnableHwEncoding, EnableKeyFrameOnlyExtraction = SavedTrickplay.EnableKeyFrameOnlyExtraction }) { Streaming = new StreamingOptions { RemoteClientBitrateLimit = SavedBitrateLimit } },
            Save,
            options => SavedTrickplay = options,
            _ => Task.FromResult<CapabilityReport?>(Report),
            () => Probing,
            () => EncoderPath,
            HistoryPath,
            TimeProvider.System,
            NullLogger.Instance)
        {
            Suggestions = (_, _) => Task.FromResult(Suggestions),
            SaveStreaming = options => SavedBitrateLimit = options.RemoteClientBitrateLimit,
        };
    }

    /// <summary>Gets or sets what the performance tests suggest.</summary>
    public IReadOnlyList<SpeedSuggestion> Suggestions { get; set; } = [];

    /// <summary>Gets where the history is kept.</summary>
    public string HistoryPath => Path.Combine(_directory, "history.json");

    /// <summary>Gets the service under test.</summary>
    public SettingsService Service { get; }

    /// <summary>Gets or sets the server's saved encoding options; VAAPI on the probed node by default.</summary>
    public EncodingOptions Saved { get; set; } = new()
    {
        HardwareAccelerationType = HardwareAccelerationType.vaapi,
        VaapiDevice = Node,
        HardwareDecodingCodecs = ["h264"],
        AllowAv1Encoding = true,
        EnableTonemapping = false,
    };

    /// <summary>Gets or sets the server's saved Internet streaming bitrate limit; 0 for none.</summary>
    public int SavedBitrateLimit { get; set; }

    /// <summary>Gets or sets the server's saved trickplay options.</summary>
    public TrickplayOptions SavedTrickplay { get; set; } = new();

    /// <summary>Gets how many times the encoding options were saved.</summary>
    public int Saves { get; private set; }

    /// <summary>Gets or sets the latest report, or null for none.</summary>
    public CapabilityReport? Report { get; set; } = Sample();

    /// <summary>Gets or sets a value indicating whether a probe is running.</summary>
    public bool Probing { get; set; }

    /// <summary>Gets or sets the server's ffmpeg path.</summary>
    public string EncoderPath { get; set; } = Ffmpeg;

    /// <summary>Builds a report: VAAPI and QSV viable on the node, NVENC not present; VAAPI advice comes from its results, with HEVC decoding and key frames passing, AV1 encoding unsupported, and tone mapping untested.</summary>
    /// <returns>The report.</returns>
    public static CapabilityReport Sample()
    {
        var vaapi = Reports.Backend(
            HwType.vaapi,
            Node,
            tier: PipelineTier.FullOpencl,
            decode: new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["hevc"] = ProbeOutcome.Pass, ["h264_keyframes"] = ProbeOutcome.Pass },
            encode: new Dictionary<string, ProbeOutcome> { ["h264"] = ProbeOutcome.Pass, ["av1"] = ProbeOutcome.CodecUnsupported });
        vaapi = vaapi with { Settings = SettingsAdvisor.For(vaapi, new AdviceContext(HostOs.Linux, InContainer: true, OpenclUnavailable: false)) };
        return Reports.Sample() with
        {
            Ffmpeg = new FfmpegSummary(Ffmpeg, "Server", "8.1.2", IsJellyfinBuild: true),
            Backends = [vaapi, Row(HwType.qsv, BackendVerdict.Viable), Row(HwType.nvenc, BackendVerdict.NotPresent) with { Device = "0" }],
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Service.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Builds a report row with no cells.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="verdict">Its verdict.</param>
    /// <returns>The row.</returns>
    private static BackendReport Row(HwType type, BackendVerdict verdict) => Reports.Backend(type, Node, verdict, PipelineTier.FullOpencl);

    /// <summary>Copies options the way the server hands out a fresh object.</summary>
    /// <param name="options">The options.</param>
    /// <returns>A copy.</returns>
    private static EncodingOptions Copy(EncodingOptions options) => new()
    {
        HardwareAccelerationType = options.HardwareAccelerationType,
        VaapiDevice = options.VaapiDevice,
        QsvDevice = options.QsvDevice,
        HardwareDecodingCodecs = [.. options.HardwareDecodingCodecs],
        AllowAv1Encoding = options.AllowAv1Encoding,
        EnableTonemapping = options.EnableTonemapping,
        EncoderPreset = options.EncoderPreset,
    };

    /// <summary>Records a save.</summary>
    /// <param name="options">The saved options.</param>
    private void Save(EncodingOptions options)
    {
        Saved = options;
        Saves++;
    }
}
