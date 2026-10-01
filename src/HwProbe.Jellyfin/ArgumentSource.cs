using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Configuration;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary><see cref="IArgumentSource"/> over upstream's own <see cref="EncodingHelper"/>.</summary>
public sealed class ArgumentSource : IArgumentSource
{
    private static readonly Dictionary<string, Func<object?[], object?>> _noHandlers = [];

    private readonly ProbeEncodingHelper _helper;
    private readonly IMediaEncoder _encoder;
    private readonly EnvironmentRules _environment;

    /// <summary>Initializes a new instance of the <see cref="ArgumentSource"/> class.</summary>
    /// <param name="capabilities">What the ffmpeg build and device support.</param>
    /// <param name="recorder">Receives every dependency member EncodingHelper reaches.</param>
    public ArgumentSource(ProbeCapabilities capabilities, CallRecorder recorder)
        : this(capabilities, recorder, EnvironmentRules.Standalone())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ArgumentSource"/> class over stubs, with explicit environment rules.</summary>
    /// <param name="capabilities">What the ffmpeg build and device support.</param>
    /// <param name="recorder">Receives every dependency member EncodingHelper reaches.</param>
    /// <param name="environment">How to treat the process environment.</param>
    internal ArgumentSource(ProbeCapabilities capabilities, CallRecorder recorder, EnvironmentRules environment)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(recorder);

        Recorder = recorder;
        _environment = environment;
        _encoder = ProbeMediaEncoder.Create(capabilities, recorder);
        _helper = new ProbeEncodingHelper(
            RecordingProxy.Create<IApplicationPaths>(recorder, _noHandlers),
            _encoder,
            RecordingProxy.Create<ISubtitleEncoder>(recorder, _noHandlers),
            RecordingProxy.Create<IConfiguration>(recorder, _noHandlers),
            RecordingProxy.Create<MediaBrowser.Common.Configuration.IConfigurationManager>(recorder, _noHandlers),
            RecordingProxy.Create<IPathManager>(recorder, _noHandlers));
    }

    /// <summary>Initializes a new instance of the <see cref="ArgumentSource"/> class over a prepared helper.</summary>
    /// <param name="helper">An EncodingHelper built from real or stub dependencies.</param>
    /// <param name="encoder">The media encoder the helper was built with, for its device traits.</param>
    /// <param name="environment">How to treat the process environment.</param>
    /// <param name="recorder">Recorder for any stubbed dependencies; unused for real ones.</param>
    internal ArgumentSource(ProbeEncodingHelper helper, IMediaEncoder encoder, EnvironmentRules environment, CallRecorder recorder)
    {
        _helper = helper;
        _encoder = encoder;
        _environment = environment;
        Recorder = recorder;
    }

    /// <summary>Gets the recorder shared by every stub.</summary>
    public CallRecorder Recorder { get; }

    /// <summary>Builds the encoding options upstream would hold for this probe.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="device">Render node or adapter, or null.</param>
    /// <param name="cell">The media shape.</param>
    /// <returns>The options.</returns>
    public static EncodingOptions CreateOptions(HwType type, string? device, ProbeCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        return new EncodingOptions
        {
            // HwType mirrors HardwareAccelerationType value-for-value.
            HardwareAccelerationType = (HardwareAccelerationType)(int)type,
            VaapiDevice = device ?? string.Empty,
            QsvDevice = device ?? string.Empty,
            EnableHardwareEncoding = cell.HardwareEncode,
            HardwareDecodingCodecs = cell.HardwareDecode ? [cell.InputCodec] : [],
            EnableDecodingColorDepth10Hevc = true,
            EnableDecodingColorDepth10Vp9 = true,
            EnableDecodingColorDepth10HevcRext = true,
            EnableDecodingColorDepth12HevcRext = true,
            EnableIntelLowPowerH264HwEncoder = cell.LowPower,
            EnableIntelLowPowerHevcHwEncoder = cell.LowPower,
            EnableTonemapping = cell.Tonemap,
            EnableVideoToolboxTonemapping = cell.Tonemap,
            AllowHevcEncoding = true,
            AllowAv1Encoding = true,
        };
    }

    /// <inheritdoc/>
    public ProbeArguments Build(HwType type, string? device, ProbeCell cell)
    {
        var options = CreateOptions(type, device, cell);
        var state = SyntheticJob.Create(cell);
        var effects = type == HwType.vaapi
            ? EncodingHelperEnvironment.Predict(type, _encoder.IsVaapiDeviceInteliHD, _encoder.IsVaapiDeviceInteli965, _encoder.IsVaapiDeviceAmd)
            : new Dictionary<string, string>();
        RefuseForeignWrites(type, effects);

        var encoder = _helper.GetVideoEncoder(state, options);
        var before = Snapshot();
        string inputArgs;
        Dictionary<string, string?> changed;
        try
        {
            inputArgs = _helper.GetInputVideoHwaccelArgs(state, options);
        }
        finally
        {
            changed = Diff(before, Snapshot());
            if (_environment.RestoreAfterGeneration)
            {
                Restore(before);
            }
        }

        var unpredicted = changed.Where(c => !effects.TryGetValue(c.Key, out var value) || value != c.Value).Select(c => c.Key).ToList();
        if (unpredicted.Count > 0)
        {
            Restore(before);
            throw new InvalidOperationException(
                $"EncodingHelper set {string.Join(", ", unpredicted)}, which EncodingHelperEnvironment.Predict doesn't cover; upstream changed.");
        }

        // v4l2m2m has no branch in GetInputVideoHwaccelArgs: always empty, encoder-only upstream.
        if (string.IsNullOrEmpty(inputArgs) && type != HwType.v4l2m2m)
        {
            throw new ArgumentConstructionException(
                $"EncodingHelper emits no {type} arguments for {cell.InputCodec} -> {encoder}; the probe would run in software.");
        }

        var filterArgs = _helper.GetVideoProcessingFilterParam(state, options, encoder);

        // Ask upstream rather than parse its output: the software encoder is what it picks with no
        // backend, and software tone-mapping (tonemapx) ignores the tone-map options, so a filter chain
        // that changes with them is a hardware tone-map.
        var softwareEncoder = _helper.GetVideoEncoder(state, CreateOptions(HwType.none, device, cell));
        var withoutTonemap = cell.Tonemap
            ? _helper.GetVideoProcessingFilterParam(state, CreateOptions(type, device, cell with { Tonemap = false }), encoder)
            : filterArgs;

        // The child gets generation's values, else the start-up values, never whatever is set right now.
        var childEnvironment = EncodingHelperEnvironment.Variables.ToDictionary(
            v => v,
            v => effects.TryGetValue(v, out var value) ? value : _environment.Baseline.GetValueOrDefault(v),
            StringComparer.Ordinal);

        return new ProbeArguments(inputArgs, filterArgs, encoder, childEnvironment)
        {
            HardwareDecoder = _helper.HardwareDecoder(state, options),
            HardwareEncoder = !string.Equals(encoder, softwareEncoder, StringComparison.Ordinal),
            HardwareTonemap = !string.Equals(filterArgs, withoutTonemap, StringComparison.Ordinal),
        };
    }

    /// <summary>Reads the variables EncodingHelper can set.</summary>
    /// <returns>Current values, null when unset.</returns>
    private static Dictionary<string, string?> Snapshot() =>
        EncodingHelperEnvironment.Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);

    /// <summary>Returns variables whose value changed.</summary>
    /// <param name="before">Values before generation.</param>
    /// <param name="after">Values after generation.</param>
    /// <returns>Changed variables with their new values.</returns>
    private static Dictionary<string, string?> Diff(Dictionary<string, string?> before, Dictionary<string, string?> after) =>
        after.Where(kv => before[kv.Key] != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Puts the variables back as they were.</summary>
    /// <param name="snapshot">Values to restore.</param>
    private static void Restore(Dictionary<string, string?> snapshot)
    {
        foreach (var (name, value) in snapshot)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>Inside the server, refuses generation that would set a variable the server's own configuration doesn't.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="effects">Variables generation will set.</param>
    private void RefuseForeignWrites(HwType type, IReadOnlyDictionary<string, string> effects)
    {
        if (_environment.RestoreAfterGeneration)
        {
            return;
        }

        var foreign = effects
            .Where(e => !_environment.ServerOwned.TryGetValue(e.Key, out var owned) || owned != e.Value)
            .Select(e => $"{e.Key}={e.Value}")
            .ToList();
        if (foreign.Count > 0)
        {
            throw new UnsafeProbeException(
                $"Generating {type} arguments for this device sets {string.Join(", ", foreign)} in the server process, which the server's own configuration doesn't. Test this device with the hwprobe CLI.");
        }
    }
}
