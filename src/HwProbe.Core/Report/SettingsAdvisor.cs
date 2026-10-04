using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Verdict;

namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>Turns one backend's test results into advice for each option on Jellyfin's Transcoding page.</summary>
/// <remarks>
/// Which options appear for which backend, their labels and their headings follow jellyfin-web's Transcoding page
/// (src/apps/dashboard/routes/playback/transcoding.tsx, features/playback/constants/codecs.ts and the en-us strings),
/// read on 2026-10-01. Trickplay options follow the Trickplay page (routes/playback/trickplay.tsx) and
/// MediaEncoder.ExtractVideoImagesOnIntervalAccelerated, read at v12.1 on 2026-10-02.
/// </remarks>
public static class SettingsAdvisor
{
    private const string DecodingSection = "Enable hardware decoding for";
    private const string EncodingSection = "Hardware encoding options";
    private const string FormatSection = "Encoding format options";
    private const string TonemapSection = "Tone mapping";
    private const string TrickplaySection = "Trickplay";
    private const string DeinterlaceSection = "Deinterlacing";
    private const string EnhancedNvdec = "EnableEnhancedNvdecDecoder";
    private const string NativeDecoder = "PreferSystemNativeHwDecoder";
    private const string NotUsed = "Not used with this backend";
    private const string NotSupported = "Not supported by this GPU";
    private const string NotTested = "Not tested";

    // Every label the advisor gives, gathered from advice for a backend of each type with no results, plus the
    // backend and device settings that "Use this backend" changes.
    private static readonly Lazy<Dictionary<string, string>> _labels = new(() =>
    {
        Dictionary<string, string> labels = new(StringComparer.Ordinal)
        {
            ["HardwareAccelerationType"] = "Hardware acceleration",
            ["VaapiDevice"] = "VA-API device",
            ["QsvDevice"] = "QSV device",
        };
        var deinterlace = new Dictionary<string, ProbeOutcome> { ["any_bwdif"] = ProbeOutcome.Untested };
        foreach (var type in Enum.GetValues<HwType>().Where(t => t != HwType.none))
        {
            var row = new BackendReport(type, string.Empty, BackendVerdict.Viable, PipelineTier.Unknown, new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), new Dictionary<string, ProbeOutcome>(), deinterlace, new Dictionary<string, ProbeOutcome>(), string.Empty);
            foreach (var advice in For(row, new AdviceContext(HostOs.Linux, InContainer: false, OpenclUnavailable: false)))
            {
                labels.TryAdd(advice.Setting, Qualified(advice));
            }
        }

        return labels;
    });

    // codecs.ts CODECS: the decoding checkboxes and the backends that show each one.
    private static readonly (string Label, string Codec, HwType[] Types)[] _codecs =
    [
        ("H264", "h264", [HwType.amf, HwType.nvenc, HwType.qsv, HwType.vaapi, HwType.rkmpp, HwType.videotoolbox, HwType.v4l2m2m]),
        ("HEVC", "hevc", [HwType.amf, HwType.nvenc, HwType.qsv, HwType.vaapi, HwType.rkmpp, HwType.videotoolbox]),
        ("MPEG1", "mpeg1video", [HwType.rkmpp]),
        ("MPEG2", "mpeg2video", [HwType.amf, HwType.nvenc, HwType.qsv, HwType.vaapi, HwType.rkmpp]),
        ("MPEG4", "mpeg4", [HwType.nvenc, HwType.rkmpp]),
        ("VC1", "vc1", [HwType.amf, HwType.nvenc, HwType.qsv, HwType.vaapi]),
        ("VP8", "vp8", [HwType.nvenc, HwType.qsv, HwType.vaapi, HwType.rkmpp, HwType.videotoolbox]),
        ("VP9", "vp9", [HwType.amf, HwType.nvenc, HwType.qsv, HwType.vaapi, HwType.rkmpp, HwType.videotoolbox]),
        ("AV1", "av1", [HwType.amf, HwType.nvenc, HwType.qsv, HwType.vaapi, HwType.rkmpp, HwType.videotoolbox]),
    ];

    // codecs.ts HEVC_VP9_HW_DECODING_TYPES and HEVC_REXT_DECODING_TYPES.
    private static readonly HwType[] _tenBitTypes = [HwType.amf, HwType.nvenc, HwType.qsv, HwType.vaapi, HwType.rkmpp];
    private static readonly HwType[] _rextTypes = [HwType.nvenc, HwType.qsv, HwType.vaapi];

    // The 10-bit and RExt options, with the decode cells that test each; each RExt option covers 4:2:2 and 4:4:4.
    private static readonly (string Label, string Setting, string[] Cells, HwType[] Types)[] _depthOptions =
    [
        ("HEVC 10bit", "EnableDecodingColorDepth10Hevc", ["hevc_10bit"], _tenBitTypes),
        ("VP9 10bit", "EnableDecodingColorDepth10Vp9", ["vp9_10bit"], _tenBitTypes),
        ("HEVC RExt 8/10bit", "EnableDecodingColorDepth10HevcRext", ["hevc_rext_10bit", "hevc_rext_444_10bit"], _rextTypes),
        ("HEVC RExt 12bit", "EnableDecodingColorDepth12HevcRext", ["hevc_rext_12bit", "hevc_rext_422_12bit"], _rextTypes),
    ];

    /// <summary>Returns the label for a setting HwProbe can change, e.g. <c>Hardware decoding: HEVC</c> for <c>HardwareDecodingCodecs:hevc</c>.</summary>
    /// <param name="setting">The setting key.</param>
    /// <returns>The settings list's label, prefixed where a bare one wouldn't place it, or a name for the backend and device settings; null for any other key.</returns>
    public static string? LabelFor(string setting) => _labels.Value.GetValueOrDefault(setting);

    /// <summary>Returns advice for every option Jellyfin's Transcoding page shows for this backend.</summary>
    /// <param name="backend">The backend's results.</param>
    /// <param name="context">Host facts that decide which fixes apply.</param>
    /// <returns>The advice in the page's order; empty for a backend that doesn't work.</returns>
    public static IReadOnlyList<SettingAdvice> For(BackendReport backend, AdviceContext context)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(context);
        if (backend.Verdict != BackendVerdict.Viable)
        {
            return [];
        }

        var type = backend.Type;
        var intel = type is HwType.qsv or HwType.vaapi;
        List<SettingAdvice> advice = [];
        foreach (var (label, codec, _) in _codecs.Where(c => c.Types.Contains(type)))
        {
            advice.Add(Advise(DecodingSection, "HardwareDecodingCodecs:" + codec, label, Cell(backend.Decode, codec)));
        }

        // Left enabled by another backend, these stay in the saved list with no checkbox to clear them.
        foreach (var (label, codec, _) in _codecs.Where(c => !c.Types.Contains(type)))
        {
            advice.Add(new SettingAdvice(DecodingSection, "HardwareDecodingCodecs:" + codec, label, SettingState.LeaveOff, "Not used with this backend") { Hidden = true });
        }

        foreach (var (label, setting, cells, _) in _depthOptions.Where(o => o.Types.Contains(type)))
        {
            advice.Add(Advise(DecodingSection, setting, label, Combined(cells.Select(c => Cell(backend.Decode, c)))));
        }

        if (type == HwType.nvenc)
        {
            advice.Add(DecoderChoice(backend, EnhancedNvdec, "Enable enhanced NVDEC decoder", "_cuvid", "cuvid"));
        }

        if (type == HwType.qsv)
        {
            advice.Add(DecoderChoice(backend, NativeDecoder, "Prefer OS native DXVA or VA-API hardware decoders", "_qsvdecoder", "QSV"));
        }

        advice.Add(Advise(EncodingSection, "EnableHardwareEncoding", "Enable hardware encoding", Cell(backend.Encode, "h264")));
        if (intel)
        {
            const string NotUsedByDriver = "Not used with this driver";

            // Jellyfin's Intel guide: on Linux, low-power mode needs the HuC firmware, and Gen 9 has low-power H.264 only.
            var huc = context.Os == HostOs.Linux ? new Uri(LowPowerAdvice.GuideUrl) : null;
            advice.Add(WithHucFix(
                Advise(EncodingSection, "EnableIntelLowPowerH264HwEncoder", "Enable Intel Low-Power H.264 hardware encoder", Cell(backend.Encode, "h264_lowpower"), NotUsedByDriver),
                huc is null || context.IntelLowPower == LowPowerSupport.None ? null : new Fix("Gen 9+: Enable HuC firmware", huc)));
            advice.Add(WithHucFix(
                Advise(EncodingSection, "EnableIntelLowPowerHevcHwEncoder", "Enable Intel Low-Power HEVC hardware encoder", Cell(backend.Encode, "hevc_lowpower"), NotUsedByDriver),
                huc is null || context.IntelLowPower != LowPowerSupport.Unknown ? null : new Fix("Gen 11+: Enable HuC firmware", huc)));
        }

        advice.Add(Advise(FormatSection, "AllowHevcEncoding", "Allow encoding in HEVC format", Cell(backend.Encode, "hevc")));
        advice.Add(Advise(FormatSection, "AllowAv1Encoding", "Allow encoding in AV1 format", Cell(backend.Encode, "av1")));

        if (type != HwType.v4l2m2m)
        {
            advice.Add(WithFix(Tonemap(backend, "EnableTonemapping", "Enable Tone mapping"), context.OpenclUnavailable ? Hints.OpenclFix(context.InContainer) : null));
        }

        if (intel)
        {
            advice.Add(Advise(TonemapSection, "EnableVppTonemapping", "Enable VPP Tone mapping", Cell(backend.Tonemap, "vpp")));
        }

        if (type == HwType.videotoolbox)
        {
            advice.Add(Tonemap(backend, "EnableVideoToolboxTonemapping", "Enable VideoToolbox Tone mapping"));
        }

        // jellyfin-web says hardware deinterlacing ignores the method, but the CUDA, OpenCL and VideoToolbox
        // deinterlacers use it (EncodingHelper.GetHwDeinterlaceFilter).
        var bwdif = backend.Deinterlace.FirstOrDefault(c => c.Key.EndsWith("_bwdif", StringComparison.Ordinal));
        if (bwdif.Key is not null)
        {
            advice.Add(Advise(DeinterlaceSection, "DeinterlaceMethod:bwdif", "Deinterlacing method: BWDIF", bwdif.Value));
        }

        advice.AddRange(Trickplay(backend, context, advice));
        return advice;
    }

    /// <summary>Advice for the Trickplay page's hardware options, which reuse the Transcoding page's settings.</summary>
    /// <param name="backend">The backend's results.</param>
    /// <param name="context">Host facts.</param>
    /// <param name="transcoding">The Transcoding page advice already given.</param>
    /// <returns>The trickplay advice.</returns>
    private static IEnumerable<SettingAdvice> Trickplay(BackendReport backend, AdviceContext context, List<SettingAdvice> transcoding)
    {
        var type = backend.Type;
        if (type == HwType.v4l2m2m)
        {
            yield return new(TrickplaySection, "Trickplay:EnableHwAcceleration", "Enable hardware decoding", SettingState.LeaveOff, NotUsed);
            yield return new(TrickplaySection, "Trickplay:EnableHwEncoding", "Enable hardware accelerated MJPEG encoding", SettingState.LeaveOff, NotUsed);
            yield break;
        }

        yield return Advise(TrickplaySection, "Trickplay:EnableHwAcceleration", "Enable hardware decoding", Cell(backend.Decode, "h264"));

        // The MJPEG encoder is only picked with the Transcoding page's hardware encoding on (EncodingHelper.GetMjpegEncoder).
        const string MjpegLabel = "Enable hardware accelerated MJPEG encoding";
        var mjpeg = backend.Encode.ContainsKey("mjpeg")
            ? Advise(TrickplaySection, "Trickplay:EnableHwEncoding", MjpegLabel, Cell(backend.Encode, "mjpeg"))
            : new(TrickplaySection, "Trickplay:EnableHwEncoding", MjpegLabel, type is HwType.nvenc or HwType.amf ? SettingState.LeaveOff : SettingState.NotTested, type is HwType.nvenc or HwType.amf ? NotUsed : NotTested);
        var encoding = transcoding.Find(a => a.Setting == "EnableHardwareEncoding");
        yield return mjpeg.State == SettingState.TurnOn && encoding?.State != SettingState.TurnOn
            ? mjpeg with { State = SettingState.LeaveOff, Note = "Requires hardware encoding" }
            : mjpeg;

        // Key-frame-only extraction quietly drops to software decoding on backends that can't do it.
        const string KeyFrameSetting = "Trickplay:EnableKeyFrameOnlyExtraction";
        const string KeyFrameLabel = "Only generate images from key frames";
        const string SoftwareNote = "Turns off hardware decoding with this backend";
        var decoder = type switch
        {
            HwType.qsv => NativeDecoder,
            HwType.nvenc => EnhancedNvdec,
            _ => null,
        };
        if ((decoder is not null && transcoding.Find(a => a.Setting == decoder)?.State != SettingState.TurnOn)
            || (type == HwType.amf && context.Os != HostOs.Windows))
        {
            yield return new(TrickplaySection, KeyFrameSetting, KeyFrameLabel, SettingState.LeaveOff, SoftwareNote);
            yield break;
        }

        // Faster but less accurate timing, so a pass only says it's safe to choose.
        var keyFrames = Advise(TrickplaySection, KeyFrameSetting, KeyFrameLabel, Cell(backend.Decode, "h264_keyframes"));
        yield return keyFrames.State == SettingState.TurnOn
            ? keyFrames with { State = SettingState.Optional, Note = "Faster, but less accurate timing" }
            : keyFrames;
    }

    /// <summary>Names an option outside the settings list, where its heading isn't there to place it.</summary>
    /// <param name="advice">The advice.</param>
    /// <returns>The label, prefixed for trickplay options and for codecs and bit depths in the decoding list.</returns>
    private static string Qualified(SettingAdvice advice) =>
        advice.Section == TrickplaySection ? "Trickplay: " + advice.Label
        : advice.Setting.StartsWith("HardwareDecodingCodecs:", StringComparison.Ordinal) || advice.Setting.StartsWith("EnableDecodingColorDepth", StringComparison.Ordinal) ? "Hardware decoding: " + advice.Label
        : advice.Label;

    /// <summary>Builds advice from one test's outcome.</summary>
    /// <param name="section">The page heading.</param>
    /// <param name="setting">The option's key.</param>
    /// <param name="label">The option's label.</param>
    /// <param name="outcome">The test's outcome, or null when there was no test.</param>
    /// <param name="notTested">The reason when nothing was tested.</param>
    /// <returns>The advice.</returns>
    private static SettingAdvice Advise(string section, string setting, string label, ProbeOutcome? outcome, string notTested = NotTested) =>
        outcome switch
        {
            ProbeOutcome.Pass => new(section, setting, label, SettingState.TurnOn, string.Empty),
            null or ProbeOutcome.Skipped or ProbeOutcome.Untested => new(section, setting, label, SettingState.NotTested, notTested),
            ProbeOutcome.CodecUnsupported => new(section, setting, label, SettingState.LeaveOff, NotSupported),
            ProbeOutcome.NotUsed => new(section, setting, label, SettingState.LeaveOff, "Software only"),
            ProbeOutcome.SoftwareFallback => new(section, setting, label, SettingState.LeaveOff, "Hardware not used"),
            ProbeOutcome.FilterUnsupported => new(section, setting, label, SettingState.LeaveOff, "Filter missing from ffmpeg"),
            ProbeOutcome.Timeout => new(section, setting, label, SettingState.LeaveOff, "Timed out"),
            ProbeOutcome.DeviceUnavailable => new(section, setting, label, SettingState.LeaveOff, "Device unavailable"),
            ProbeOutcome.PermissionDenied => new(section, setting, label, SettingState.LeaveOff, "No permission to use the device"),
            _ => new(section, setting, label, SettingState.LeaveOff, "Test failed"),
        };

    /// <summary>Attaches a fix to advice that says to leave an option off.</summary>
    /// <param name="advice">The advice.</param>
    /// <param name="fix">The fix, or null.</param>
    /// <returns>The advice, with the fix only when the option failed.</returns>
    private static SettingAdvice WithFix(SettingAdvice advice, Fix? fix) =>
        advice.State == SettingState.LeaveOff && fix is not null ? advice with { Fix = fix } : advice;

    /// <summary>Attaches the HuC fix to a failed low-power encoder option.</summary>
    /// <param name="advice">The advice.</param>
    /// <param name="fix">The fix, or null when the GPU's generation has no such encoder.</param>
    /// <returns>The advice; with a fix, an unsupported result says the firmware is what's missing.</returns>
    private static SettingAdvice WithHucFix(SettingAdvice advice, Fix? fix)
    {
        var fixedAdvice = WithFix(advice, fix);
        return fixedAdvice.Fix is not null && fixedAdvice.Note == NotSupported ? fixedAdvice with { Note = "Requires HuC firmware" } : fixedAdvice;
    }

    /// <summary>Advice for an option that picks between two hardware decoders, from tests of each.</summary>
    /// <param name="backend">The backend's results.</param>
    /// <param name="setting">The option's key; on selects the plain decode cells.</param>
    /// <param name="label">The option's label.</param>
    /// <param name="suffix">The suffix of the decode cells run with the option off.</param>
    /// <param name="other">Names the decoders used with the option off.</param>
    /// <returns>Leave off when some codec decodes only with the option off.</returns>
    private static SettingAdvice DecoderChoice(BackendReport backend, string setting, string label, string suffix, string other)
    {
        var pairs = backend.Decode.Keys
            .Where(k => k.EndsWith(suffix, StringComparison.Ordinal))
            .Select(k => (On: k[..^suffix.Length], Off: k))
            .ToList();
        if (pairs.Count == 0)
        {
            return new(DecodingSection, setting, label, SettingState.NotTested, NotTested);
        }

        var onlyOff = pairs
            .Where(p => backend.Decode[p.Off] == ProbeOutcome.Pass && Cell(backend.Decode, p.On) != ProbeOutcome.Pass)
            .Select(p => CellLabel(p.On))
            .ToList();
        return onlyOff.Count == 0
            ? new(DecodingSection, setting, label, SettingState.TurnOn, string.Empty)
            : new(DecodingSection, setting, label, SettingState.LeaveOff, $"{other} decoders needed for " + string.Join(", ", onlyOff));
    }

    /// <summary>Advice for a tone-mapping option, from any tone-map test except VPP.</summary>
    /// <param name="backend">The backend's results.</param>
    /// <param name="setting">The option's key.</param>
    /// <param name="label">The option's label.</param>
    /// <returns>The advice.</returns>
    private static SettingAdvice Tonemap(BackendReport backend, string setting, string label)
    {
        var cells = backend.Tonemap.Where(c => c.Key != "vpp").Select(c => c.Value).ToList();
        ProbeOutcome? outcome = cells.Count == 0 ? null : cells.Contains(ProbeOutcome.Pass) ? ProbeOutcome.Pass : cells[0];

        // The engine only tone-maps after a 10-bit decode passes.
        var notTested = Cell(backend.Decode, "hevc_10bit") == ProbeOutcome.Pass ? "Not used with this setup" : "Requires HEVC 10bit decoding";
        return Advise(TonemapSection, setting, label, outcome, notTested);
    }

    /// <summary>Combines the tests behind one option: formats Jellyfin decodes in software don't count, and every other one must pass.</summary>
    /// <param name="outcomes">The tests' outcomes, null where a test didn't run.</param>
    /// <returns>The first failure, else the first untested result, else a pass; NotUsed when Jellyfin uses software for all of them.</returns>
    private static ProbeOutcome? Combined(IEnumerable<ProbeOutcome?> outcomes)
    {
        var all = outcomes.ToList();
        var used = all.Where(o => o != ProbeOutcome.NotUsed).ToList();
        if (used.Count == 0)
        {
            return all.Count > 0 ? ProbeOutcome.NotUsed : null;
        }

        if (used.Exists(o => o is not (null or ProbeOutcome.Pass or ProbeOutcome.Skipped or ProbeOutcome.Untested)))
        {
            return used.First(o => o is not (null or ProbeOutcome.Pass or ProbeOutcome.Skipped or ProbeOutcome.Untested));
        }

        // A missing test is null, which advises "not tested".
        return used.Exists(o => o != ProbeOutcome.Pass) ? used.First(o => o != ProbeOutcome.Pass) : ProbeOutcome.Pass;
    }

    /// <summary>Looks up a cell's outcome.</summary>
    /// <param name="cells">A column of results.</param>
    /// <param name="key">The cell key.</param>
    /// <returns>The outcome, or null when the cell wasn't tested.</returns>
    private static ProbeOutcome? Cell(IReadOnlyDictionary<string, ProbeOutcome> cells, string key) =>
        cells.TryGetValue(key, out var outcome) ? outcome : null;

    /// <summary>Names a decode cell the way the Transcoding page does.</summary>
    /// <param name="cell">The cell key, e.g. <c>hevc_10bit</c>.</param>
    /// <returns>The label, e.g. <c>HEVC 10bit</c>.</returns>
    private static string CellLabel(string cell) =>
        _codecs.FirstOrDefault(c => c.Codec == cell).Label
        ?? _depthOptions.FirstOrDefault(o => o.Cells?.Contains(cell) == true).Label
        ?? cell;
}
