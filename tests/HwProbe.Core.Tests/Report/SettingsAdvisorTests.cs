using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Verdict;
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
                "Enable hardware decoding", "Enable hardware accelerated MJPEG encoding",
            ],
            labels);
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
    [InlineData("Enable Intel Low-Power HEVC hardware encoder", SettingState.LeaveOff, "Not supported by this GPU")]
    [InlineData("Allow encoding in AV1 format", SettingState.LeaveOff, "Not supported by this GPU")]
    [InlineData("Enable Tone mapping", SettingState.TurnOn, "")]
    [InlineData("Enable VPP Tone mapping", SettingState.LeaveOff, "Test failed")]
    public void AdviceFollowsResults(string label, SettingState state, string note)
    {
        var advice = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv, _docker), a => a.Label == label);

        Assert.Equal((state, note), (advice.State, advice.Note));
    }

    /// <summary>A codec only the QSV decoders handle means the native-decoder option should stay off.</summary>
    [Fact]
    public void QsvOnlyCodecKeepsNativeDecodersOff()
    {
        var decode = new Dictionary<string, ProbeOutcome>(_apolloLakeQsv.Decode) { ["vc1"] = U, ["vc1_qsvdecoder"] = P };

        var advice = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Decode = decode }, _docker), a => a.Setting == "PreferSystemNativeHwDecoder");

        Assert.Equal((SettingState.LeaveOff, "QSV decoders needed for VC1"), (advice.State, advice.Note));
    }

    /// <summary>Options with no test are marked untested rather than guessed.</summary>
    [Fact]
    public void MissingTestsAreNotTested()
    {
        var advice = SettingsAdvisor.For(_apolloLakeQsv with { Tonemap = new Dictionary<string, ProbeOutcome>(), Decode = new Dictionary<string, ProbeOutcome>() }, _docker);

        Assert.Equal(SettingState.NotTested, Assert.Single(advice, a => a.Setting == "EnableTonemapping").State);
        Assert.Equal("Needs HEVC 10bit decoding", Assert.Single(advice, a => a.Setting == "EnableTonemapping").Note);
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

        var lowPower = Assert.Single(advice, a => a.Setting == "EnableIntelLowPowerHevcHwEncoder").Fix!;
        Assert.Equal("Gen 11+: enable HuC firmware", lowPower.Action);
        Assert.EndsWith("#configure-and-verify-lp-mode-on-linux", lowPower.Url!.ToString(), StringComparison.Ordinal);
        Assert.Equal(Hints.OpenclFix(inContainer: true), Assert.Single(advice, a => a.Setting == "EnableTonemapping").Fix);
        Assert.Null(Assert.Single(advice, a => a.Setting == "EnableVppTonemapping").Fix);
        Assert.Null(Assert.Single(advice, a => a.Setting == "EnableIntelLowPowerH264HwEncoder").Fix);
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

        Assert.Equal((SettingState.LeaveOff, "Needs hardware encoding"), (advice.State, advice.Note));
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

    /// <summary>Key-frame-only trickplay is flagged only where it would turn hardware decoding off.</summary>
    [Fact]
    public void KeyFrameOnlyFlaggedWhereItDropsHardwareDecoding()
    {
        const string Setting = "Trickplay:EnableKeyFrameOnlyExtraction";
        var qsvOnly = new Dictionary<string, ProbeOutcome>(_apolloLakeQsv.Decode) { ["vc1"] = U, ["vc1_qsvdecoder"] = P };

        var native = SettingsAdvisor.For(_apolloLakeQsv, _docker);
        var qsvDecoders = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Decode = qsvOnly }, _docker), a => a.Setting == Setting);
        var nvenc = Assert.Single(SettingsAdvisor.For(_apolloLakeQsv with { Type = HwType.nvenc }, _docker), a => a.Setting == Setting);

        Assert.DoesNotContain(native, a => a.Setting == Setting);
        Assert.DoesNotContain(SettingsAdvisor.For(_apolloLakeQsv with { Type = HwType.vaapi }, _docker), a => a.Setting == Setting);
        Assert.Equal((SettingState.LeaveOff, "Turns off hardware decoding with this backend"), (qsvDecoders.State, qsvDecoders.Note));
        Assert.Equal(SettingState.NotTested, nvenc.State);
    }
}
