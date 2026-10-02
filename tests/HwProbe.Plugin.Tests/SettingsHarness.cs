using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Settings;
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
            () => new ServerSettings(Copy(Saved), new TrickplayOptions { EnableHwAcceleration = SavedTrickplay.EnableHwAcceleration, EnableHwEncoding = SavedTrickplay.EnableHwEncoding, EnableKeyFrameOnlyExtraction = SavedTrickplay.EnableKeyFrameOnlyExtraction }),
            Save,
            options => SavedTrickplay = options,
            _ => Task.FromResult<CapabilityReport?>(Report),
            () => Probing,
            () => EncoderPath,
            Path.Combine(_directory, "history.json"),
            TimeProvider.System,
            NullLogger.Instance);
    }

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

    /// <summary>Builds a report: VAAPI and QSV viable on the node, NVENC not present; VAAPI advice covers four options.</summary>
    /// <returns>The report.</returns>
    public static CapabilityReport Sample()
    {
        var vaapi = Row(HwType.vaapi, BackendVerdict.Viable) with
        {
            Settings =
            [
                new("Enable hardware decoding for", "HardwareDecodingCodecs:hevc", "HEVC", SettingState.TurnOn, string.Empty),
                new("Encoding format options", "AllowAv1Encoding", "Allow encoding in AV1 format", SettingState.LeaveOff, "Not supported by this GPU"),
                new("Tone mapping", "EnableTonemapping", "Enable Tone mapping", SettingState.NotTested, "Not tested"),
                new("Trickplay", "Trickplay:EnableHwAcceleration", "Enable hardware decoding", SettingState.TurnOn, string.Empty),
            ],
        };
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
    private static BackendReport Row(HwType type, BackendVerdict verdict) => new(
        type,
        Node,
        verdict,
        PipelineTier.FullOpencl,
        new Dictionary<string, ProbeOutcome>(),
        new Dictionary<string, ProbeOutcome>(),
        new Dictionary<string, ProbeOutcome>(),
        new Dictionary<string, ProbeOutcome>(),
        new Dictionary<string, ProbeOutcome>(),
        string.Empty);

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
