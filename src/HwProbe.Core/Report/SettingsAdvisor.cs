using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Verdict;

namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>Turns one backend's test results into advice for each option on Jellyfin's Transcoding page.</summary>
/// <remarks>
/// Which options appear for which backend, their labels and their headings follow jellyfin-web's Transcoding page
/// (src/apps/dashboard/routes/playback/transcoding.tsx, features/playback/constants/codecs.ts and the en-us strings),
/// read on 2026-10-01.
/// </remarks>
public static class SettingsAdvisor
{
    private const string DecodingSection = "Enable hardware decoding for";
    private const string EncodingSection = "Hardware encoding options";
    private const string FormatSection = "Encoding format options";
    private const string TonemapSection = "Tone mapping";
    private const string NotTested = "Not tested";

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

    // The 10-bit and RExt options, with the decode cell that tests each.
    private static readonly (string Label, string Setting, string Cell, HwType[] Types)[] _depthOptions =
    [
        ("HEVC 10bit", "EnableDecodingColorDepth10Hevc", "hevc_10bit", _tenBitTypes),
        ("VP9 10bit", "EnableDecodingColorDepth10Vp9", "vp9_10bit", _tenBitTypes),
        ("HEVC RExt 8/10bit", "EnableDecodingColorDepth10HevcRext", "hevc_rext_10bit", _rextTypes),
        ("HEVC RExt 12bit", "EnableDecodingColorDepth12HevcRext", "hevc_rext_12bit", _rextTypes),
    ];

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

        foreach (var (label, setting, cell, _) in _depthOptions.Where(o => o.Types.Contains(type)))
        {
            advice.Add(Advise(DecodingSection, setting, label, Cell(backend.Decode, cell)));
        }

        if (type == HwType.qsv)
        {
            advice.Add(NativeDecoders(backend));
        }

        advice.Add(Advise(EncodingSection, "EnableHardwareEncoding", "Enable hardware encoding", Cell(backend.Encode, "h264")));
        if (intel)
        {
            const string NotUsed = "Not used with this driver";

            // Jellyfin's Intel guide: on Linux, low-power mode needs the HuC firmware, and Gen 9 has low-power H.264 only.
            var huc = context.Os == HostOs.Linux ? new Uri(LowPowerAdvice.GuideUrl) : null;
            advice.Add(WithFix(
                Advise(EncodingSection, "EnableIntelLowPowerH264HwEncoder", "Enable Intel Low-Power H.264 hardware encoder", Cell(backend.Encode, "h264_lowpower"), NotUsed),
                huc is null ? null : new Fix("Enable HuC firmware", huc)));
            advice.Add(WithFix(
                Advise(EncodingSection, "EnableIntelLowPowerHevcHwEncoder", "Enable Intel Low-Power HEVC hardware encoder", Cell(backend.Encode, "hevc_lowpower"), NotUsed),
                huc is null ? null : new Fix("Gen 11+: enable HuC firmware", huc)));
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

        return advice;
    }

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
            ProbeOutcome.CodecUnsupported => new(section, setting, label, SettingState.LeaveOff, "Not supported by this GPU"),
            _ => new(section, setting, label, SettingState.LeaveOff, "Test failed"),
        };

    /// <summary>Attaches a fix to advice that says to leave an option off.</summary>
    /// <param name="advice">The advice.</param>
    /// <param name="fix">The fix, or null.</param>
    /// <returns>The advice, with the fix only when the option failed.</returns>
    private static SettingAdvice WithFix(SettingAdvice advice, Fix? fix) =>
        advice.State == SettingState.LeaveOff && fix is not null ? advice with { Fix = fix } : advice;

    /// <summary>Advice for "Prefer OS native DXVA or VA-API hardware decoders", from the native and QSV decoder results.</summary>
    /// <param name="backend">The QSV backend's results.</param>
    /// <returns>Leave off when some codec decodes only with the QSV decoders.</returns>
    private static SettingAdvice NativeDecoders(BackendReport backend)
    {
        const string Setting = "PreferSystemNativeHwDecoder";
        const string Label = "Prefer OS native DXVA or VA-API hardware decoders";
        const string Suffix = "_qsvdecoder";
        var pairs = backend.Decode.Keys
            .Where(k => k.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(k => (Native: k[..^Suffix.Length], Qsv: k))
            .ToList();
        if (pairs.Count == 0)
        {
            return new(DecodingSection, Setting, Label, SettingState.NotTested, NotTested);
        }

        var onlyQsv = pairs
            .Where(p => backend.Decode[p.Qsv] == ProbeOutcome.Pass && Cell(backend.Decode, p.Native) != ProbeOutcome.Pass)
            .Select(p => CellLabel(p.Native))
            .ToList();
        return onlyQsv.Count == 0
            ? new(DecodingSection, Setting, Label, SettingState.TurnOn, string.Empty)
            : new(DecodingSection, Setting, Label, SettingState.LeaveOff, "QSV decoders needed for " + string.Join(", ", onlyQsv));
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
        var notTested = Cell(backend.Decode, "hevc_10bit") == ProbeOutcome.Pass ? "Not used with this setup" : "Needs HEVC 10bit decoding";
        return Advise(TonemapSection, setting, label, outcome, notTested);
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
        ?? _depthOptions.FirstOrDefault(o => o.Cell == cell).Label
        ?? cell;
}
