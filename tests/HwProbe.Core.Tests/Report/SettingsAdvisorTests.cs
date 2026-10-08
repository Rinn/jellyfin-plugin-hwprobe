using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Report;

/// <summary>Advice for the Transcoding page options, from real results on an Intel Apollo Lake GPU.</summary>
[Trait("Category", "Unit")]
public sealed class SettingsAdvisorTests
{
    private const ProbeOutcome P = ProbeOutcome.Pass;
    private const ProbeOutcome U = ProbeOutcome.CodecUnsupported;

    private static readonly AdviceContext _docker = new(HostOs.Linux, InContainer: true, OpenclUnavailable: false);

    /// <summary>QSV on the Apollo Lake host, as probed on 2026-10-01.</summary>
    private static readonly BackendReport _apolloLakeQsv = new(
        HwType.qsv,
        "/dev/dri/renderD128",
        BackendVerdict.Viable,
        PipelineTier.FullOpencl,
        new Dictionary<string, ProbeOutcome>
        {
            ["h264"] = P,
            ["hevc"] = P,
            ["mpeg2video"] = P,
            ["vc1"] = P,
            ["vp8"] = P,
            ["vp9"] = P,
            ["av1"] = U,
            ["hevc_10bit"] = P,
            ["vp9_10bit"] = U,
            ["hevc_rext_10bit"] = U,
            ["hevc_rext_12bit"] = U,
            ["av1_10bit"] = U,
            ["h264_qsvdecoder"] = P,
            ["hevc_qsvdecoder"] = P,
            ["av1_qsvdecoder"] = U,
            ["hevc_10bit_qsvdecoder"] = P,
        },
        new Dictionary<string, ProbeOutcome> { ["h264"] = P, ["hevc"] = P, ["hevc_10bit"] = P, ["av1"] = U, ["h264_lowpower"] = P, ["hevc_lowpower"] = U },
        new Dictionary<string, ProbeOutcome> { ["opencl"] = P, ["vpp"] = ProbeOutcome.FilterUnsupported },
        new Dictionary<string, ProbeOutcome> { ["vaapi"] = P },
        new Dictionary<string, ProbeOutcome> { ["text"] = P },
        string.Empty);

    /// <summary>Every QSV option on the Transcoding page is listed once, in the page's order.</summary>
    [Fact]
    public void ListsEveryQsvOptionInPageOrder()
    {
        var labels = SettingsAdvisor.For(_apolloLakeQsv, _docker).Where(a => !a.Hidden).Select(a => a.Label);

        Assert.Equal(
            [
                "H264", "HEVC", "MPEG2", "VC1", "VP8", "VP9", "AV1",
                "HEVC 10bit", "VP9 10bit", "HEVC RExt 8/10bit", "HEVC RExt 12bit",
                "Prefer OS native DXVA or VA-API hardware decoders",
                "Enable hardware encoding", "Enable Intel Low-Power H.264 hardware encoder", "Enable Intel Low-Power HEVC hardware encoder",
                "Allow encoding in HEVC format", "Allow encoding in AV1 format",
                "Enable Tone mapping", "Enable VPP Tone mapping",
                "Allow subtitle extraction on the fly",
                "Enable hardware decoding", "Enable hardware accelerated MJPEG encoding", "Only generate images from key frames",
            ],
            labels);
    }

    /// <summary>Software advice covers the options Jellyfin's pages show with no hardware backend: HEVC and AV1 off, BWDIF and key frames from their tests, and subtitles extracted rather than burned in.</summary>
    [Fact]
    public void SoftwareAdvisesTheOptionsShownWithNoBackend()
    {
        var software = Reports.Backend(HwType.none, decode: new Dictionary<string, ProbeOutcome> { ["h264_keyframes"] = P }, deinterlace: new Dictionary<string, ProbeOutcome> { ["bwdif"] = ProbeOutcome.CodecUnsupported });

        var advice = SettingsAdvisor.For(software, _docker).Select(a => (a.Setting, a.State, a.Note));

        Assert.Equal(
            [
                ("AllowHevcEncoding", SettingState.LeaveOff, "Slow on the CPU"),
                ("AllowAv1Encoding", SettingState.LeaveOff, "Slow on the CPU"),
                ("DeinterlaceMethod:bwdif", SettingState.LeaveOff, "Test failed"),
                ("EnableSubtitleExtraction", SettingState.TurnOn, "Avoids burning in text subtitles"),
                ("Trickplay:EnableHwAcceleration", SettingState.LeaveOff, "Not used with this backend"),
                ("Trickplay:EnableHwEncoding", SettingState.LeaveOff, "Not used with this backend"),
                ("Trickplay:EnableKeyFrameOnlyExtraction", SettingState.Optional, "Faster, but less accurate timing"),
            ],
            advice);
    }

    /// <summary>Passing tests turn options on; unsupported codecs and failed tests leave them off with a short reason.</summary>
    /// <param name="label">The option.</param>
    /// <param name="state">The expected advice.</param>
    /// <param name="note">The expected reason.</param>
    [Theory]
    [InlineData("HEVC 10bit", SettingState.TurnOn, "")]
    [InlineData("AV1", SettingState.LeaveOff, "Not supported by this GPU")]
    [InlineData("VP9 10bit", SettingState.LeaveOff, "Not supported by this GPU")]
    [InlineData("Prefer OS native DXVA or VA-API hardware decoders", SettingState.TurnOn, "")]
    [InlineData("Enable Intel Low-Power H.264 hardware encoder", SettingState.TurnOn, "")]
    [InlineData("Enable Intel Low-Power HEVC hardware encoder", SettingState.LeaveOff, "Requires HuC firmware")]
    [InlineData("Allow encoding in AV1 format", SettingState.LeaveOff, "Not supported by this GPU")]
    [InlineData("Enable Tone mapping", SettingState.TurnOn, "")]
    [InlineData("Enable VPP Tone mapping", SettingState.LeaveOff, "Filter missing from ffmpeg")]
    public void AdviceFollowsResults(string label, SettingState state, string note)
    {
        var advice = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv, _docker), a => a.Label == label);

        Assert.Equal((state, note), (advice.State, advice.Note));
    }

    /// <summary>Native decoders stay on, as Dolby Vision tone mapping requires, naming the codecs only the QSV decoders handle.</summary>
    [Fact]
    public void QsvOnlyCodecIsNamedWithNativeDecodersOn()
    {
        var decode = new Dictionary<string, ProbeOutcome>(_apolloLakeQsv.Decode) { ["vc1"] = U, ["vc1_qsvdecoder"] = P };
        var two = new Dictionary<string, ProbeOutcome>(decode) { ["mpeg2video"] = U, ["mpeg2video_qsvdecoder"] = P };

        var advice = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Decode = decode }, _docker), a => a.Setting == "PreferSystemNativeHwDecoder");
        var both = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Decode = two }, _docker), a => a.Setting == "PreferSystemNativeHwDecoder");

        Assert.Equal((SettingState.TurnOn, "VC1 requires QSV decoders"), (advice.State, advice.Note));
        Assert.Equal("MPEG2 and VC1 require QSV decoders", both.Note);
    }

    /// <summary>Options with no test are marked untested rather than guessed.</summary>
    [Fact]
    public void MissingTestsAreNotTested()
    {
        var advice = SettingsAdvisor.For(_apolloLakeQsv with { Tonemap = new Dictionary<string, ProbeOutcome>(), Decode = new Dictionary<string, ProbeOutcome>() }, _docker);

        Assert.Equal(SettingState.NotTested, Assert.Single(advice, a => a.Setting == "EnableTonemapping").State);
        Assert.Equal("Requires HEVC 10bit decoding", Assert.Single(advice, a => a.Setting == "EnableTonemapping").Note);
        Assert.Equal(SettingState.NotTested, Assert.Single(advice, a => a.Setting == "HardwareDecodingCodecs:h264").State);
    }

    /// <summary>Codecs the Transcoding page doesn't show for the backend are hidden and left off.</summary>
    [Fact]
    public void HiddenCodecsAreLeftOff()
    {
        var hidden = SettingsAdvisor.For(_apolloLakeQsv, _docker).Where(a => a.Hidden).ToList();

        Assert.Equal(["HardwareDecodingCodecs:mpeg1video", "HardwareDecodingCodecs:mpeg4"], hidden.Select(a => a.Setting));
        Assert.All(hidden, a => Assert.Equal(SettingState.LeaveOff, a.State));
    }

    /// <summary>Failed options that a host change could fix carry a short fix with a documentation link.</summary>
    [Fact]
    public void ActionableFailuresCarryFixes()
    {
        var tonemap = new Dictionary<string, ProbeOutcome> { ["opencl"] = ProbeOutcome.FilterUnsupported, ["vpp"] = ProbeOutcome.FilterUnsupported };

        var advice = SettingsAdvisor.For(_apolloLakeQsv with { Tonemap = tonemap }, _docker with { OpenclUnavailable = true });

        var lowPower = Assert.Single(advice, a => a.Setting == "EnableIntelLowPowerHevcHwEncoder").Fix;
        Assert.NotNull(lowPower);
        Assert.Equal("Gen 11+: Enable HuC firmware", lowPower.Action);
        Assert.EndsWith("#configure-and-verify-lp-mode-on-linux", lowPower.Url?.ToString(), StringComparison.Ordinal);
        Assert.Equal(Hints.OpenclFix(inContainer: true), Assert.Single(advice, a => a.Setting == "EnableTonemapping").Fix);
        Assert.Null(Assert.Single(advice, a => a.Setting == "EnableVppTonemapping").Fix);
        Assert.Null(Assert.Single(advice, a => a.Setting == "EnableIntelLowPowerH264HwEncoder").Fix);
    }

    /// <summary>Low-power encoders a GPU's generation doesn't have get no firmware fix; unknown GPUs keep it.</summary>
    /// <param name="support">The generation's low-power support.</param>
    /// <param name="h264Fix">Whether a failed low-power H.264 gets the fix.</param>
    /// <param name="hevcFix">Whether a failed low-power HEVC gets the fix.</param>
    [Theory]
    [InlineData(LowPowerSupport.Unknown, true, true)]
    [InlineData(LowPowerSupport.H264Only, true, false)]
    [InlineData(LowPowerSupport.None, false, false)]
    public void LowPowerFixFollowsGeneration(LowPowerSupport support, bool h264Fix, bool hevcFix)
    {
        var encode = new Dictionary<string, ProbeOutcome>(_apolloLakeQsv.Encode) { ["h264_lowpower"] = U, ["hevc_lowpower"] = U };

        var advice = SettingsAdvisor.For(_apolloLakeQsv with { Encode = encode }, _docker with { IntelLowPower = support });

        var h264 = Assert.Single(advice, a => a.Setting == "EnableIntelLowPowerH264HwEncoder");
        var hevc = Assert.Single(advice, a => a.Setting == "EnableIntelLowPowerHevcHwEncoder");

        Assert.Equal(h264Fix ? ("Gen 9+: Enable HuC firmware", "Requires HuC firmware") : (null, "Not supported by this GPU"), (h264.Fix?.Action, h264.Note));
        Assert.Equal(hevcFix ? ("Gen 11+: Enable HuC firmware", "Requires HuC firmware") : (null, "Not supported by this GPU"), (hevc.Fix?.Action, hevc.Note));
    }

    /// <summary>Each backend gets the options its Transcoding page shows, and a backend that doesn't work gets none.</summary>
    [Fact]
    public void OptionsDependOnBackend()
    {
        var nvenc = SettingsAdvisor.For(_apolloLakeQsv with { Type = HwType.nvenc }, _docker).Where(a => !a.Hidden).Select(a => a.Setting).ToList();
        var videotoolbox = SettingsAdvisor.For(_apolloLakeQsv with { Type = HwType.videotoolbox }, _docker).Where(a => !a.Hidden).Select(a => a.Setting).ToList();

        Assert.Contains("HardwareDecodingCodecs:mpeg4", nvenc);
        Assert.DoesNotContain("EnableIntelLowPowerH264HwEncoder", nvenc);
        Assert.DoesNotContain("PreferSystemNativeHwDecoder", nvenc);
        Assert.Contains("EnableVideoToolboxTonemapping", videotoolbox);
        Assert.DoesNotContain("HardwareDecodingCodecs:vc1", videotoolbox);
        Assert.Empty(SettingsAdvisor.For(_apolloLakeQsv with { Verdict = BackendVerdict.NotPresent }, _docker));
    }

    /// <summary>Trickplay hardware decoding follows the H.264 decode test, and MJPEG encoding its own test.</summary>
    [Fact]
    public void TrickplayFollowsResults()
    {
        var encode = new Dictionary<string, ProbeOutcome>(_apolloLakeQsv.Encode) { ["mjpeg"] = P };

        var advice = SettingsAdvisor.For(_apolloLakeQsv with { Encode = encode }, _docker);

        Assert.Equal(SettingState.TurnOn, Assert.Single(advice, a => a.Setting == "Trickplay:EnableHwAcceleration").State);
        Assert.Equal(SettingState.TurnOn, Assert.Single(advice, a => a.Setting == "Trickplay:EnableHwEncoding").State);
        Assert.All(advice.Where(a => a.Setting.StartsWith("Trickplay:", StringComparison.Ordinal)), a => Assert.Equal("Trickplay", a.Section));
    }

    /// <summary>MJPEG encoding is only picked with hardware encoding on, so a passing test can't help without it.</summary>
    [Fact]
    public void TrickplayEncodingNeedsHardwareEncoding()
    {
        var encode = new Dictionary<string, ProbeOutcome>(_apolloLakeQsv.Encode) { ["h264"] = U, ["mjpeg"] = P };

        var advice = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Encode = encode }, _docker), a => a.Setting == "Trickplay:EnableHwEncoding");

        Assert.Equal((SettingState.LeaveOff, "Requires hardware encoding"), (advice.State, advice.Note));
    }

    /// <summary>NVENC and AMF have no MJPEG encoder in Jellyfin, so trickplay hardware encoding does nothing there.</summary>
    /// <param name="type">The backend.</param>
    [Theory]
    [InlineData(HwType.nvenc)]
    [InlineData(HwType.amf)]
    public void TrickplayEncodingNotUsed(HwType type)
    {
        var advice = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Type = type }, _docker), a => a.Setting == "Trickplay:EnableHwEncoding");

        Assert.Equal((SettingState.LeaveOff, "Not used with this backend"), (advice.State, advice.Note));
    }

    /// <summary>Key-frame-only trickplay is flagged where it would turn hardware decoding off, and optional where its test passes.</summary>
    [Fact]
    public void KeyFrameOnlyFollowsItsTestAndTheDecoder()
    {
        const string Setting = "Trickplay:EnableKeyFrameOnlyExtraction";
        var native = SettingsAdvisor.For(_apolloLakeQsv, _docker);

        // With no cuvid results, enhanced NVDEC isn't tested, so key frames may drop to software.
        var nvenc = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Type = HwType.nvenc }, _docker), a => a.Setting == Setting);

        var passed = new Dictionary<string, ProbeOutcome>(_apolloLakeQsv.Decode) { ["h264_keyframes"] = P };
        var optional = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Type = HwType.vaapi, Decode = passed }, _docker), a => a.Setting == Setting);

        Assert.Equal(SettingState.NotTested, Assert.Single(native, a => a.Setting == Setting).State);
        Assert.Equal((SettingState.Optional, "Faster, but less accurate timing"), (optional.State, optional.Note));
        Assert.Equal((SettingState.LeaveOff, "Turns off hardware decoding with this backend"), (nvenc.State, nvenc.Note));
    }

    /// <summary>Enhanced NVDEC is advised like the native decoders: on, naming a codec that decodes with cuvid alone.</summary>
    [Fact]
    public void EnhancedNvdecFollowsCuvidResults()
    {
        var nvenc = _apolloLakeQsv with { Type = HwType.nvenc, Decode = new Dictionary<string, ProbeOutcome> { ["h264"] = P, ["h264_cuvid"] = P, ["vc1"] = U, ["vc1_cuvid"] = P } };

        var advice = SettingsAdvisor.For(nvenc, _docker);

        var nvdec = Assert.Single(advice, a => a.Setting == "EnableEnhancedNvdecDecoder");
        Assert.Equal((SettingState.TurnOn, "VC1 requires cuvid decoders"), (nvdec.State, nvdec.Note));
        Assert.Equal(SettingState.NotTested, Assert.Single(advice, a => a.Setting == "Trickplay:EnableKeyFrameOnlyExtraction").State);
        Assert.Equal(SettingState.TurnOn, Assert.Single(SettingsAdvisor.For(nvenc with { Decode = new Dictionary<string, ProbeOutcome> { ["h264"] = P, ["h264_cuvid"] = P } }, _docker), a => a.Setting == "EnableEnhancedNvdecDecoder").State);
    }

    /// <summary>The BWDIF test decides the deinterlacing method, and there's no advice where the method isn't used.</summary>
    [Fact]
    public void BwdifFollowsItsTest()
    {
        var videotoolbox = _apolloLakeQsv with { Type = HwType.videotoolbox, Deinterlace = new Dictionary<string, ProbeOutcome> { ["videotoolbox"] = P, ["videotoolbox_bwdif"] = P } };

        var advice = Assert.Single(SettingsAdvisor.For(videotoolbox, _docker), a => a.Setting == "DeinterlaceMethod:bwdif");

        Assert.Equal(("Deinterlacing", SettingState.TurnOn), (advice.Section, advice.State));
        Assert.DoesNotContain(SettingsAdvisor.For(_apolloLakeQsv, _docker), a => a.Setting == "DeinterlaceMethod:bwdif");
    }

    /// <summary>An RExt option needs both its 4:2:2 and 4:4:4 tests, ignoring a format Jellyfin decodes in software.</summary>
    /// <param name="yuv422">The 4:2:2 10-bit result.</param>
    /// <param name="yuv444">The 4:4:4 10-bit result, or null when it didn't run.</param>
    /// <param name="state">The expected advice.</param>
    /// <param name="note">The expected reason.</param>
    [Theory]
    [InlineData(ProbeOutcome.NotUsed, ProbeOutcome.Pass, SettingState.TurnOn, "")]
    [InlineData(ProbeOutcome.Pass, ProbeOutcome.CodecUnsupported, SettingState.LeaveOff, "Not supported by this GPU")]
    [InlineData(ProbeOutcome.Pass, null, SettingState.NotTested, "Not tested")]
    [InlineData(ProbeOutcome.NotUsed, ProbeOutcome.NotUsed, SettingState.LeaveOff, "Software only in this ffmpeg")]
    public void RextNeedsEveryFormatJellyfinDecodesInHardware(ProbeOutcome yuv422, ProbeOutcome? yuv444, SettingState state, string note)
    {
        var decode = new Dictionary<string, ProbeOutcome>(_apolloLakeQsv.Decode) { ["hevc_rext_10bit"] = yuv422 };
        if (yuv444 is { } outcome)
        {
            decode["hevc_rext_444_10bit"] = outcome;
        }

        var advice = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Decode = decode }, _docker), a => a.Setting == "EnableDecodingColorDepth10HevcRext");

        Assert.Equal((state, note), (advice.State, advice.Note));
    }

    /// <summary>Settings named where a bare label wouldn't place them are prefixed, the backend's device settings are named, and other keys have none.</summary>
    /// <param name="setting">The setting key.</param>
    /// <param name="label">The label, or null.</param>
    [Theory]
    [InlineData("HardwareDecodingCodecs:hevc", "Hardware decoding: HEVC")]
    [InlineData("DeinterlaceMethod:bwdif", "Deinterlacing method: Bob Weaver DeInterlacing Filter (BWDIF)")]
    [InlineData("Trickplay:EnableHwAcceleration", "Trickplay: Enable hardware decoding")]
    [InlineData("QsvDevice", "QSV device")]
    [InlineData("VaapiDevice", "VA-API device")]
    [InlineData("EncoderAppPath", null)]
    public void LabelForNamesSettings(string setting, string? label) =>
        Assert.Equal(label, SettingsAdvisor.LabelFor(setting));

    /// <summary>A performance test option that stands for a server setting the advisor also names has the advisor's label, so the page names it alike in both places.</summary>
    [Fact]
    public void CatalogOptionLabelsMatchTheAdvisor()
    {
        var shared = Catalog.Default.Options.Select(o => (o.Label, Advisor: o.Server is { } server ? SettingsAdvisor.LabelFor(server) : null)).Where(o => o.Advisor is not null).ToList();

        Assert.NotEmpty(shared);
        Assert.All(shared, o => Assert.Equal(o.Label, o.Advisor));
    }

    /// <summary>Every setting the advisor gives, for every backend, host and result, has a label for the history.</summary>
    [Fact]
    public void LabelForNamesEveryAdvisedSetting()
    {
        var settings = Reports.AdvisedSettings();

        Assert.NotEmpty(settings);
        Assert.All(settings, s => Assert.NotNull(SettingsAdvisor.LabelFor(s)));
    }
}
