using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Configuration;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary><see cref="IArgumentSource"/> over upstream's own <see cref="EncodingHelper"/>.</summary>
public sealed class ArgumentSource : IArgumentSource
{
    // Emitted by EncodingHelper.GetVideoQualityParam (v12.2, L2185) when it allows low-power encoding.
    private const string LowPowerArg = "-low_power 1";

    // Stands for the output file in an audio command line, which the caller replaces.
    private const string AudioOutput = "hwprobe-output";

    private static readonly Dictionary<string, Func<object?[], object?>> _noHandlers = [];

    // No font attachments to extract: the burn-in filter then omits fontsdir (EncodingHelper.GetTextSubtitlesFilter).
    private static readonly Dictionary<string, Func<object?[], object?>> _pathHandlers = new() { ["GetAttachmentFolderPath"] = _ => null };

    // Unset, as on a server without them: GetInputArgument reads the analyse duration and probe size (ConfigurationExtensions, v12.2).
    private static readonly Dictionary<string, Func<object?[], object?>> _configurationHandlers = new() { ["get_Item"] = _ => null };

    // Every suffix the filter chains pass to GetHwDeinterlaceFilter (EncodingHelper.cs, v12.2, L4135-5920).
    private static readonly string[] _deinterlaceFamilies = ["vaapi", "qsv", "cuda", "videotoolbox", "opencl"];

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
            RecordingProxy.Create<IConfiguration>(recorder, _configurationHandlers),
            RecordingProxy.Create<MediaBrowser.Common.Configuration.IConfigurationManager>(recorder, _noHandlers),
            RecordingProxy.Create<IPathManager>(recorder, _pathHandlers));
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

    /// <summary>Gets a value indicating whether the build takes <c>-hwaccel_flags +low_priority</c>, which image extraction adds for VideoToolbox.</summary>
    public bool LowPriorityHwDecode { get; init; }

    /// <summary>Builds the encoding options upstream would hold for this probe.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="device">Render node or adapter, or null.</param>
    /// <param name="cell">The media shape.</param>
    /// <returns>The options.</returns>
    public static EncodingOptions CreateOptions(HwType type, string? device, ProbeCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var defaults = new EncodingOptions();
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
            EnableVideoToolboxTonemapping = cell.VideoToolboxTonemap ?? cell.Tonemap,
            TonemappingAlgorithm = cell.TonemapAlgorithm is null ? defaults.TonemappingAlgorithm : Enum.Parse<TonemappingAlgorithm>(cell.TonemapAlgorithm),
            TonemappingMode = cell.TonemapMode is null ? defaults.TonemappingMode : Enum.Parse<TonemappingMode>(cell.TonemapMode),
            TonemappingRange = cell.TonemapRange is null ? defaults.TonemappingRange : Enum.Parse<TonemappingRange>(cell.TonemapRange),
            TonemappingDesat = cell.TonemapDesat ?? defaults.TonemappingDesat,
            TonemappingPeak = cell.TonemapPeak ?? defaults.TonemappingPeak,
            TonemappingParam = cell.TonemapParam ?? defaults.TonemappingParam,
            DownMixStereoAlgorithm = cell.DownmixAlgorithm is null ? defaults.DownMixStereoAlgorithm : Enum.Parse<DownMixStereoAlgorithms>(cell.DownmixAlgorithm),
            DownMixAudioBoost = cell.DownmixBoost ?? defaults.DownMixAudioBoost,
            EnableVppTonemapping = cell.VppTonemap,
            PreferSystemNativeHwDecoder = cell.PreferNativeDecoder,
            EnableEnhancedNvdecDecoder = cell.EnhancedNvdec,
            DeinterlaceMethod = cell.Bwdif ? DeinterlaceMethod.bwdif : DeinterlaceMethod.yadif,
            AllowHevcEncoding = true,
            AllowAv1Encoding = true,
            EncoderPreset = cell.EncoderPreset is null ? EncoderPreset.auto : Enum.Parse<EncoderPreset>(cell.EncoderPreset),
            H264Crf = cell.H264Crf,
            H265Crf = cell.H265Crf,
            EnableAudioVbr = cell.AudioVbr,
            DeinterlaceDoubleRate = cell.DoubleRate,
            EncodingThreadCount = cell.EncodingThreadCount,
        };
    }

    /// <inheritdoc/>
    public ProbeArguments Build(HwType type, string? device, ProbeCell cell)
    {
        var options = CreateOptions(type, device, cell);
        var state = SyntheticJob.Create(cell, cell.SourcePath);
        var encoder = _helper.GetVideoEncoder(state, options);
        int? outputWidth = null;
        if (cell.FullQuality)
        {
            // As StreamingHelpers.GetStreamingState sets them for a real request (v12.2).
            state.OutputVideoBitrate = _helper.GetVideoBitrateParamValue(state.BaseRequest, state.VideoStream, state.OutputVideoCodec);
            state.OutputAudioBitrate = _helper.GetAudioBitrateParam(null, state.OutputAudioCodec, state.AudioStream, state.OutputAudioChannels);

            // The server picks the size from the bitrate and codec, as StreamingHelpers.GetStreamingState does (v12.2).
            if (state.OutputVideoBitrate is { } bitrate)
            {
                var request = state.BaseRequest;
                var notRequested = request.Width is null && request.Height is null && request.MaxWidth is null && request.MaxHeight is null;
                if (notRequested && request.VideoBitRate is { } asked && state.VideoStream?.BitRate is { } source && asked >= source)
                {
                    request.MaxWidth = state.VideoStream.Width;
                    request.MaxHeight = state.VideoStream.Height;
                }
                else
                {
                    var resolution = ResolutionNormalizer.Normalize(state.VideoStream?.BitRate, bitrate, EncodingHelper.ScaleBitrate(bitrate, state.OutputVideoCodec, "h264"), request.MaxWidth, request.MaxHeight, state.TargetFramerate);
                    request.MaxWidth = resolution.MaxWidth;
                    request.MaxHeight = resolution.MaxHeight;
                }

                outputWidth = request.MaxWidth;
            }
        }

        // A probe cell has no source path (the probe names its clip itself), and GetInputArgument requires one.
        var ((inputArgs, inputArgument), childEnvironment) = Generate(type, () => (
            _helper.GetInputVideoHwaccelArgs(state, options),
            cell.FullQuality && cell.SourcePath is not null ? _helper.GetInputArgument(state, options, null) : null));

        // v4l2m2m has no branch in GetInputVideoHwaccelArgs: always empty, encoder-only upstream.
        // Software (none) is measured for speed, and has no hardware arguments by design.
        if (string.IsNullOrEmpty(inputArgs) && type != HwType.v4l2m2m && type != HwType.none)
        {
            throw new ArgumentConstructionException(
                $"EncodingHelper emits no {type} arguments for {cell.InputCodec} -> {encoder}; the probe would run in software.");
        }

        var filterArgs = _helper.GetVideoProcessingFilterParam(state, options, encoder);

        // Ask upstream rather than parse its output: the software encoder is what it picks with no
        // backend, and software tone-mapping (tonemapx) ignores the tone-map options, so a filter chain
        // that changes with them is a hardware tone-map.
        var softwareEncoder = _helper.GetVideoEncoder(state, CreateOptions(HwType.none, device, cell));

        // A VPP cell is compared with VPP off rather than tone-mapping off: when VPP can't be used Jellyfin
        // falls back to OpenCL, and that is the plain tone-map cell's result, not VPP's.
        var withoutTonemap = cell.Tonemap
            ? _helper.GetVideoProcessingFilterParam(state, CreateOptions(type, device, cell.VppTonemap ? cell with { VppTonemap = false } : cell with { Tonemap = false }), encoder)
            : filterArgs;

        // Only the low-power flag is taken from the quality arguments, so a low-power cell differs from
        // its plain cell by that flag alone.
        var lowPower = cell.LowPower
            && _helper.GetVideoQualityParam(state, encoder, options, EncoderPreset.veryfast).Contains(LowPowerArg, StringComparison.Ordinal);

        // MediaEncoder.ExtractVideoImagesOnIntervalAccelerated puts this before the hwaccel arguments.
        if (cell.KeyFramesOnly)
        {
            inputArgs = "-skip_frame nokey " + inputArgs;
        }

        return new ProbeArguments(inputArgs, filterArgs, encoder, childEnvironment)
        {
            EncoderArgs = cell.FullQuality ? " " + _helper.GetVideoQualityParam(state, encoder, options, EncoderPreset.veryfast).Trim()
                : lowPower ? " " + LowPowerArg : string.Empty,
            AudioArgs = cell.FullQuality && state.AudioStream is not null ? " " + _helper.GetProgressiveVideoAudioArguments(state, options).Trim() : string.Empty,
            InputArgument = inputArgument,
            Threads = cell.FullQuality ? EncodingHelper.GetNumberOfThreads(state, options, encoder) : null,
            LowPowerEncoder = lowPower,
            HardwareDecoder = _helper.HardwareDecoder(state, options),
            HardwareEncoder = !string.Equals(encoder, softwareEncoder, StringComparison.Ordinal),
            HardwareTonemap = !string.Equals(filterArgs, withoutTonemap, StringComparison.Ordinal),
            HardwareDeinterlacer = cell.Interlaced ? HardwareDeinterlacer(state, options, filterArgs) : null,
            OutputWidth = outputWidth,
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A pinned copy of what MediaEncoder.ExtractVideoImagesOnIntervalAccelerated and ExtractVideoImagesOnIntervalInternal
    /// add around EncodingHelper's input, filter and encoder (v12.2): the key-frame check, the job, the input threads and
    /// low priority, the stream map, setpts, and the quality per encoder. The output is left to the caller.
    /// </remarks>
    public ProbeArguments BuildImages(HwType type, string? device, ProbeCell cell, ImageJob job)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(job);
        var path = cell.SourcePath ?? throw new ArgumentException("Image arguments require the source path.", nameof(cell));
        var allowHwAccel = type != HwType.none;
        var options = allowHwAccel ? CreateOptions(type, device, cell) : new EncodingOptions();
        if (allowHwAccel && job.KeyFramesOnly && !SupportsKeyFrameOnly(options))
        {
            throw new ArgumentConstructionException($"{type} decoders don't decode key frames only here, so images are extracted in software.");
        }

        // A new EncodingOptions, as upstream makes for software, with the fields that have no defaults set.
        if (!allowHwAccel)
        {
            options.EnableHardwareEncoding = false;
            options.HardwareAccelerationType = HardwareAccelerationType.none;
            options.EnableTonemapping = false;
        }

        var source = SyntheticJob.Create(cell with { Audio = false }, path);
        var stream = source.VideoStream ?? throw new InvalidOperationException("The synthetic job has no video stream.");
        var state = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            IsVideoRequest = true,
            MediaSource = source.MediaSource,
            VideoStream = stream,
            BaseRequest = new BaseEncodingJobOptions { MaxWidth = job.Width, MaxFramerate = (float)(1.0 / TimeSpan.FromMilliseconds(job.IntervalMilliseconds).TotalSeconds) },
            MediaPath = path,
            OutputVideoCodec = "mjpeg",
        };
        var encoder = job.HwEncoding ? _helper.GetVideoEncoder(state, options) : state.OutputVideoCodec;
        var ((hwaccel, input), childEnvironment) = Generate(type, () => (_helper.GetInputVideoHwaccelArgs(state, options), _helper.GetInputArgument(state, options, source.MediaSource.Container).Trim()));
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("EncodingHelper returned empty input arguments.");
        }

        // MediaEncoder passes TrickplayOptions.ProcessThreads, an int, so its own default thread count never applies.
        if (!allowHwAccel)
        {
            input = string.Create(CultureInfo.InvariantCulture, $"-threads {job.Threads} {input}");
        }

        if (options.HardwareAccelerationType == HardwareAccelerationType.videotoolbox && LowPriorityHwDecode)
        {
            input = "-hwaccel_flags +low_priority " + input;
        }

        input += " -map 0:" + EncodingHelper.FindIndex(source.MediaSource.MediaStreams, stream).ToString(CultureInfo.InvariantCulture);
        var filter = _helper.GetVideoProcessingFilterParam(state, options, encoder).Trim();
        if (!job.KeyFramesOnly)
        {
            var fps = filter.IndexOf("fps=", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(filter) || fps < 0)
            {
                throw new InvalidOperationException("EncodingHelper returned invalid filter parameters.");
            }

            var rate = stream.ReferenceFrameRate is > 0 and var reference ? reference : 30;
            filter = filter.Insert(fps, string.Create(CultureInfo.InvariantCulture, $"setpts=N/{rate:F3}/TB,"));
        }

        var (option, quality) = ImageQuality(encoder, job.Qscale);
        var allowSoftware = encoder.Contains("videotoolbox", StringComparison.InvariantCultureIgnoreCase) ? "-allow_sw 1 " : string.Empty;
        return new ProbeArguments(hwaccel, filter, encoder, childEnvironment)
        {
            InputArgument = (job.KeyFramesOnly ? "-skip_frame nokey " : string.Empty) + input,
            EncoderArgs = string.Create(CultureInfo.InvariantCulture, $"{option}{quality} {allowSoftware}{EncodingHelper.GetVideoSyncOption("0", _encoder.EncoderVersion).Trim()}"),
            Threads = job.Threads,
            HardwareDecoder = _helper.HardwareDecoder(state, options),
            HardwareEncoder = !string.Equals(encoder, state.OutputVideoCodec, StringComparison.Ordinal),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The job is set up as UniversalAudioController and StreamingHelpers.GetStreamingState set it for a progressive stream with no
    /// bitrate asked (v12.2), and the command is AudioHelper's: EncodingHelper.GetProgressiveAudioFullCommandLine, split at its input.
    /// </remarks>
    public AudioArguments BuildAudio(AudioCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var options = new EncodingOptions
        {
            EnableAudioVbr = cell.AudioVbr,
            DownMixStereoAlgorithm = Enum.Parse<DownMixStereoAlgorithms>(cell.DownmixAlgorithm),
            DownMixAudioBoost = cell.DownmixBoost,
            EncodingThreadCount = cell.EncodingThreadCount,
        };
        var stream = new MediaStream { Index = 0, Type = MediaStreamType.Audio, Codec = cell.Codec, Channels = cell.Channels, SampleRate = cell.SampleRate };
        var state = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            MediaSource = new MediaSourceInfo { Id = "hwprobe", Protocol = MediaProtocol.File, Path = cell.SourcePath, MediaStreams = [stream] },
            MediaPath = cell.SourcePath,
            AudioStream = stream,
            OutputAudioCodec = cell.OutputCodec,
            OutputContainer = cell.OutputCodec,
            BaseRequest = new BaseEncodingJobOptions { AudioCodec = cell.OutputCodec, MaxAudioChannels = cell.OutputChannels, AudioChannels = cell.OutputChannels, EnableAudioVbrEncoding = true },
        };
        var ((modifier, input), environment) = Generate(HwType.none, () => (_helper.GetInputModifier(state, options, null), _helper.GetInputArgument(state, options, null)));
        var head = $"{modifier} {input}".Trim();
        if (cell.OutputCodec is not { } codec)
        {
            return new AudioArguments(head, string.Empty, environment);
        }

        state.OutputAudioChannels = _helper.GetNumAudioChannelsParam(state, stream, codec);
        state.OutputAudioBitrate = EncodingHelper.LosslessAudioCodecs.Contains(codec, StringComparer.OrdinalIgnoreCase)
            ? stream.BitRate ?? 0
            : _helper.GetAudioBitrateParam(null, codec, stream, state.OutputAudioChannels) ?? 0;
        var (full, _) = Generate(HwType.none, () => _helper.GetProgressiveAudioFullCommandLine(state, options, AudioOutput));
        var tail = $" -y \"{AudioOutput}\"";
        if (!full.StartsWith(head, StringComparison.Ordinal) || !full.EndsWith(tail, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"EncodingHelper.GetProgressiveAudioFullCommandLine no longer starts with the input and ends with the output; upstream changed: {full}");
        }

        return new AudioArguments(head, full[head.Length..^tail.Length].Trim(), environment);
    }

    /// <summary>Returns the quality option and value image extraction passes to an encoder, as MediaEncoder.ExtractVideoImagesOnIntervalInternal maps the quality scale (v12.2).</summary>
    /// <param name="encoder">The MJPEG encoder.</param>
    /// <param name="qscale">The quality scale (TrickplayOptions.Qscale).</param>
    /// <returns>The option with its trailing space, and the value.</returns>
    internal static (string Option, int Value) ImageQuality(string encoder, int qscale)
    {
        var quality = Math.Clamp(qscale, 1, 31);
        bool Uses(string name) => encoder.Contains(name, StringComparison.OrdinalIgnoreCase);

        // The integer divisions are upstream's: 100 / 30 is 3.
        return Uses("vaapi") || Uses("qsv") ? ("-global_quality:v ", 100 - ((quality - 1) * (100 / 30)))
            : Uses("videotoolbox") ? ("-qscale:v ", 118 - ((quality - 1) * (118 / 30)))
            : Uses("rkmpp") ? ("-qp_init:v ", 99 - ((quality - 1) * (99 / 30)))
            : ("-qscale:v ", quality);
    }

    /// <summary>Returns whether a backend decodes key frames only, as MediaEncoder.ExtractVideoImagesOnIntervalAccelerated checks it (v12.2).</summary>
    /// <param name="options">The server's encoding options.</param>
    /// <returns>False when image extraction would turn hardware decoding off.</returns>
    private static bool SupportsKeyFrameOnly(EncodingOptions options) => options.HardwareAccelerationType switch
    {
        HardwareAccelerationType.nvenc => options.EnableEnhancedNvdecDecoder,
        HardwareAccelerationType.amf => OperatingSystem.IsWindows(),
        HardwareAccelerationType.qsv => options.PreferSystemNativeHwDecoder,
        HardwareAccelerationType.vaapi or HardwareAccelerationType.videotoolbox or HardwareAccelerationType.rkmpp => true,
        _ => false,
    };

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

    /// <summary>Runs generation that may set EncodingHelper's environment variables, inside one snapshot, and checks it set only what was predicted.</summary>
    /// <typeparam name="T">What generation returns.</typeparam>
    /// <param name="type">The backend.</param>
    /// <param name="generate">The generation.</param>
    /// <returns>Its result, and the environment the child must run with: generation's values, else the start-up values, never whatever is set right now.</returns>
    private (T Value, Dictionary<string, string?> Environment) Generate<T>(HwType type, Func<T> generate)
    {
        var effects = type == HwType.vaapi
            ? EncodingHelperEnvironment.Predict(type, _encoder.IsVaapiDeviceInteliHD, _encoder.IsVaapiDeviceInteli965, _encoder.IsVaapiDeviceAmd)
            : new Dictionary<string, string>();
        RefuseForeignWrites(type, effects);

        var before = Snapshot();
        T value;
        Dictionary<string, string?> changed;
        try
        {
            value = generate();
        }
        finally
        {
            changed = Diff(before, Snapshot());
            if (_environment.RestoreAfterGeneration)
            {
                Restore(before);
            }
        }

        var unpredicted = changed.Where(c => !effects.TryGetValue(c.Key, out var set) || set != c.Value).Select(c => c.Key).ToList();
        if (unpredicted.Count > 0)
        {
            Restore(before);
            throw new InvalidOperationException(
                $"EncodingHelper set {string.Join(", ", unpredicted)}, which EncodingHelperEnvironment.Predict doesn't cover; upstream changed.");
        }

        var environment = EncodingHelperEnvironment.Variables.ToDictionary(
            v => v,
            v => effects.TryGetValue(v, out var set) ? set : _environment.Baseline.GetValueOrDefault(v),
            StringComparer.Ordinal);
        return (value, environment);
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

    /// <summary>Returns the hardware filter family whose deinterlace filter appears in the generated chain.</summary>
    /// <param name="state">The job.</param>
    /// <param name="options">Encoding options.</param>
    /// <param name="filterArgs">The generated filter arguments.</param>
    /// <returns>The family, e.g. <c>vaapi</c>, or null when deinterlacing isn't done in hardware.</returns>
    private string? HardwareDeinterlacer(EncodingJobInfo state, EncodingOptions options, string filterArgs) =>
        _deinterlaceFamilies.FirstOrDefault(family =>
            _helper.GetHwDeinterlaceFilter(state, options, family) is { Length: > 0 } filter
            && filterArgs.Contains(filter, StringComparison.Ordinal));
}
