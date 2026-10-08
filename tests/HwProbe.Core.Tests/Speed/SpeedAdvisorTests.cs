using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>Suggestions from measured results in <see cref="SpeedAdvisor"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SpeedAdvisorTests
{
    private static readonly DateTimeOffset _time = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The thresholds the suggestions judge by.</summary>
    private static readonly CatalogAdvice _advice = Catalog.Default.Advice;

    /// <summary>A faster hardware backend is suggested; software never is, even when it's faster; and nothing is suggested without the configured backend's result to compare.</summary>
    [Fact]
    public void SuggestsHardwareNeverSoftware()
    {
        var run = Run(new SpeedSettings(), Result(HwType.vaapi, "a", 100), Result(HwType.qsv, "a", 200), Result(HwType.none, "a", 400));

        var onVaapi = SpeedAdvisor.Advise(run, [run], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings());
        var onSoftware = SpeedAdvisor.Advise(run, [run], HwType.none, string.Empty, new SpeedSettings());
        var onQsv = SpeedAdvisor.Advise(run, [run], HwType.qsv, "/dev/dri/renderD128", new SpeedSettings());

        Assert.Equal(HwType.qsv, Assert.Single(onVaapi, s => s.Kind == SpeedSuggestionKind.FastestBackend).Type);
        Assert.Equal(HwType.qsv, Assert.Single(onSoftware, s => s.Kind == SpeedSuggestionKind.FastestBackend).Type);
        Assert.DoesNotContain(onQsv, s => s.Kind == SpeedSuggestionKind.FastestBackend);

        // Nothing to compare with when the configured backend wasn't measured.
        Assert.DoesNotContain(SpeedAdvisor.Advise(run, [run], HwType.nvenc, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FastestBackend);
    }

    /// <summary>QSV is suggested over VAAPI on the same GPU when they measure alike, but not when VAAPI is faster by more than noise; VAAPI is never suggested over QSV that measures alike.</summary>
    [Fact]
    public void PrefersQsvOverVaapi()
    {
        var alike = Run(new SpeedSettings(), Result(HwType.vaapi, "a", 100), Result(HwType.qsv, "a", 100 * (1 - (_advice.Noise / 2))));
        var vaapiFaster = Run(new SpeedSettings(), Result(HwType.vaapi, "a", 100), Result(HwType.qsv, "a", 100 * (1 - (_advice.Noise * 4))));

        var preferred = Assert.Single(SpeedAdvisor.Advise(alike, [alike], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FastestBackend);
        Assert.Equal((HwType.qsv, true), (preferred.Type, preferred.Preferred));
        Assert.DoesNotContain(SpeedAdvisor.Advise(vaapiFaster, [vaapiFaster], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FastestBackend);
        Assert.DoesNotContain(SpeedAdvisor.Advise(alike, [alike], HwType.qsv, "/dev/dri/renderD128", new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FastestBackend);
        Assert.Equal(HwType.qsv, Assert.Single(SpeedAdvisor.Advise(alike, [alike], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FastestBackend).Type);
    }

    /// <summary>With VAAPI configured, settings compared on QSV runs (where suites run instead) are suggested.</summary>
    [Fact]
    public void ComparesQsvRunsForVaapi()
    {
        const string Film = "live-action|h264-8mbps";
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.qsv, Film, 30) with { Command = "medium" });
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.qsv, Film, 45) with { Command = "fast" });

        var faster = Assert.Single(SpeedAdvisor.Advise(fast, [medium, fast], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings { EncoderPreset = "medium" }), s => s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal(("EncoderPreset", "fast"), (faster.Setting, faster.Value));
    }

    /// <summary>A backend that measures alike with the configured one is suggested when it used less, with what it saved; a setting value likewise.</summary>
    [Fact]
    public void PrefersWhatUsesLess()
    {
        const string Film = "live-action|h264-8mbps";
        static SpeedResult Used(SpeedResult r, double cpuSeconds, long memory) => r with { Resources = new Core.Resources.ResourceUsage(10, cpuSeconds, memory) };
        var run = Run(new SpeedSettings(), Used(Result(HwType.vaapi, Film, 100), 8, 400_000_000), Used(Result(HwType.nvenc, Film, 100 * (1 + (_advice.Noise / 2))), 4, 400_000_000) with { Device = string.Empty });

        var backend = Assert.Single(SpeedAdvisor.Advise(run, [run], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FastestBackend);
        Assert.Equal((HwType.nvenc, false), (backend.Type, backend.Preferred));
        Assert.Equal([("Cpu", 0.5)], backend.Savings.Select(x => (x.Resource, Math.Round(x.Fraction, 2))));

        // Using more memory cancels the saving.
        var mixed = Run(new SpeedSettings(), Used(Result(HwType.vaapi, Film, 100), 8, 400_000_000), Used(Result(HwType.nvenc, Film, 100 * (1 + (_advice.Noise / 2))), 4, 800_000_000) with { Device = string.Empty });
        Assert.DoesNotContain(SpeedAdvisor.Advise(mixed, [mixed], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FastestBackend);

        var on = Run(new SpeedSettings(), Used(Result(HwType.none, Film, 100), 40, 600_000_000) with { Command = "on" });
        var off = Run(new SpeedSettings { EnhancedNvdec = false }, Used(Result(HwType.none, Film, 100 * (1 - (_advice.Noise / 2))), 40 * (1 - (_advice.ResourceMargin * 2)), (long)(600_000_000 * (1 - (_advice.ResourceMargin * 1.5)))) with { Command = "off" });
        var efficient = Assert.Single(SpeedAdvisor.Advise(off, [on, off], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.EfficientSetting);
        Assert.Equal(("EnhancedNvdec", "false"), (efficient.Setting, efficient.Value));
        Assert.Equal(["Cpu", "Memory"], efficient.Savings.Select(x => x.Resource));
    }

    /// <summary>The thread count recommends Auto whenever it was measured, even beside a limit that measured as fast with less CPU.</summary>
    [Fact]
    public void RecommendsAutoThreads()
    {
        const string Film = "live-action|h264-8mbps";
        static SpeedResult Used(SpeedResult r, double cpuSeconds) => r with { Resources = new Core.Resources.ResourceUsage(10, cpuSeconds, 400_000_000) };
        var auto = Run(new SpeedSettings(), Used(Result(HwType.none, Film, 100), 40) with { Command = "auto" });
        var four = Run(new SpeedSettings { EncodingThreadCount = 4 }, Used(Result(HwType.none, Film, 100), 20) with { Command = "four" });

        var threads = Assert.Single(SpeedAdvisor.Advise(four, [auto, four], HwType.none, string.Empty, new SpeedSettings { EncodingThreadCount = 4 }), s => s.Setting == "EncodingThreadCount");

        Assert.Equal((SpeedSuggestionKind.RecommendedValue, "-1"), (threads.Kind, threads.Value));
        Assert.Equal("4", Assert.Single(threads.Compared).Value);
    }

    /// <summary>VBR audio is suggested as the better-quality value when it still keeps up, beside VBR off, which avoids its caveat.</summary>
    [Fact]
    public void SuggestsVbrAudio()
    {
        const string Film = "live-action|h264-8mbps";
        var off = Run(new SpeedSettings(), Result(HwType.none, Film, 430) with { Command = "cbr" });
        var on = Run(new SpeedSettings { AudioVbr = true }, Result(HwType.none, Film, 425) with { Command = "vbr" });

        var advice = SpeedAdvisor.Advise(on, [off, on], HwType.none, string.Empty, new SpeedSettings());

        var quality = Assert.Single(advice, s => s.Kind == SpeedSuggestionKind.HigherQuality);
        Assert.Equal(("AudioVbr", "true"), (quality.Setting, quality.Value));
        var compatible = Assert.Single(advice, s => s.Kind == SpeedSuggestionKind.Compatible);
        Assert.Equal(("AudioVbr", "false", true), (compatible.Setting, compatible.Value, compatible.Current));
    }

    /// <summary>VBR audio that is current and more efficient still comes with VBR off, which avoids its caveat.</summary>
    [Fact]
    public void OffersVbrOffBesideEfficientVbr()
    {
        const string Film = "live-action|h264-8mbps";
        static SpeedResult Used(SpeedResult r, double cpuSeconds) => r with { Resources = new Core.Resources.ResourceUsage(10, cpuSeconds, 400_000_000) };
        var off = Run(new SpeedSettings(), Used(Result(HwType.none, Film, 430), 10) with { Command = "cbr" });
        var on = Run(new SpeedSettings { AudioVbr = true }, Used(Result(HwType.none, Film, 430), 10 * (1 - (_advice.ResourceMargin * 2))) with { Command = "vbr" });

        var advice = SpeedAdvisor.Advise(on, [off, on], HwType.none, string.Empty, new SpeedSettings { AudioVbr = true });

        var efficient = Assert.Single(advice, s => s.Kind == SpeedSuggestionKind.EfficientSetting);
        Assert.Equal(("true", true), (efficient.Value, efficient.Current));
        var compatible = Assert.Single(advice, s => s.Kind == SpeedSuggestionKind.Compatible);
        Assert.Equal(("false", false, efficient.Speed), (compatible.Value, compatible.Current, compatible.Speed));
    }

    /// <summary>A server value that wins is reported as current, and a better-quality value that costs many concurrent streams isn't suggested.</summary>
    [Fact]
    public void ReportsTheCurrentValueAndGuardsStreams()
    {
        const string Film = "live-action|h264-8mbps";
        var lostStreams = (int)(12 * (1 - (_advice.MaxStreamLoss * 1.5)));
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 150) with { Command = "fast", Streams = 12 });
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 100) with { Command = "medium", Streams = lostStreams });

        var advice = SpeedAdvisor.Advise(fast, [fast, medium], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "fast" });

        var current = Assert.Single(advice, s => s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal(("fast", true, 12, lostStreams), (current.Value, current.Current, current.Streams, current.OtherStreams));
        Assert.DoesNotContain(advice, s => s.Kind == SpeedSuggestionKind.HigherQuality);
    }

    /// <summary>Switching from VPP to the general tone-mapping method can be suggested; it still tone maps.</summary>
    [Fact]
    public void SwitchesToneMappingMethod()
    {
        const string Film = "live-action|h264-8mbps";
        var vpp = Run(new SpeedSettings { Tonemap = true, VppTonemap = true }, Result(HwType.qsv, Film, 100) with { Command = "vpp" });
        var opencl = Run(new SpeedSettings { Tonemap = true, VppTonemap = false }, Result(HwType.qsv, Film, 150) with { Command = "opencl" });

        var faster = Assert.Single(SpeedAdvisor.Advise(opencl, [vpp, opencl], HwType.qsv, "/dev/dri/renderD128", new SpeedSettings { Tonemap = true, VppTonemap = true }), s => s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal(("VppTonemap", "false"), (faster.Setting, faster.Value));
    }

    /// <summary>The Intel low power suite switches both codecs at once, and each is still compared on its own codec's outputs.</summary>
    [Fact]
    public void ComparesLowPowerPerCodec()
    {
        const string H264 = "drama|h264-8mbps";
        const string Hevc = "drama|hevc-8mbps";
        var off = Run(new SpeedSettings { QsvLowPowerH264 = false, QsvLowPowerHevc = false }, Result(HwType.qsv, H264, 100) with { Command = "h264" }, Result(HwType.qsv, Hevc, 100) with { Command = "hevc" });
        var on = Run(new SpeedSettings { QsvLowPowerH264 = true, QsvLowPowerHevc = true }, Result(HwType.qsv, H264, 150) with { Command = "h264-lp" }, Result(HwType.qsv, Hevc, 150) with { Command = "hevc-lp" });

        var advice = SpeedAdvisor.Advise(on, [off, on], HwType.qsv, "/dev/dri/renderD128", new SpeedSettings { QsvLowPowerH264 = false, QsvLowPowerHevc = false });

        Assert.Equal(["drama|h264-8mbps"], Assert.Single(advice, s => s.Setting == "QsvLowPowerH264" && s.Value == "true").Outputs);
        Assert.Equal(["drama|hevc-8mbps"], Assert.Single(advice, s => s.Setting == "QsvLowPowerHevc" && s.Value == "true").Outputs);
    }

    /// <summary>Switching to a group's choice sets its conditions and the values it applies, and turns off a method that would still take precedence.</summary>
    [Fact]
    public void WorksOutTheChangesForAGroupChoice()
    {
        var server = new SpeedSettings { Tonemap = true, VideoToolboxTonemap = true };

        Assert.Equal([("VideoToolboxTonemap", "false")], SpeedAdvisor.GroupRowChanges("tonemap", "Tone mapping", server, HwType.videotoolbox));
        Assert.Equal([("VppTonemap", "true"), ("Tonemap", "true")], SpeedAdvisor.GroupRowChanges("tonemap", "VPP", new SpeedSettings { Tonemap = false }, HwType.qsv));
        Assert.Equal([("Tonemap", "false"), ("VideoToolboxTonemap", "false")], SpeedAdvisor.GroupRowChanges("tonemap", "Off", server, HwType.videotoolbox));
        Assert.Null(SpeedAdvisor.GroupRowChanges("tonemap", "VPP", server, HwType.videotoolbox));
        Assert.Equal([("DeinterlaceMethod", "yadif"), ("DoubleRate", "false")], SpeedAdvisor.GroupRowChanges("deinterlace", "Yet Another DeInterlacing Filter (YADIF), single rate", new SpeedSettings { Bwdif = true, DoubleRate = true }, HwType.none));
    }

    /// <summary>Each deinterlacing choice keeps its own row: double rate measured with YADIF and with BWDIF aren't merged into one.</summary>
    [Fact]
    public void KeepsEveryDeinterlacingChoice()
    {
        const string Film = "sports-576i|h264-8mbps";
        SpeedReport Step(bool bwdif, bool doubled, double fps) => Run(new SpeedSettings { Bwdif = bwdif, DoubleRate = doubled }, Result(HwType.none, Film, fps) with { Command = $"{bwdif}{doubled}", FrameRate = doubled ? 50 : 25 });
        SpeedReport[] runs = [Step(false, false, 400), Step(false, true, 500), Step(true, false, 300), Step(true, true, 400)];

        var advice = SpeedAdvisor.Advise(runs[3], runs, HwType.none, string.Empty, new SpeedSettings { Bwdif = true, DoubleRate = true });

        var rows = advice.Where(s => s.Group == "deinterlace").SelectMany(s => s.Compared.Select(c => c.Row).Append(s.Row)).Distinct().ToList();
        Assert.Contains("Yet Another DeInterlacing Filter (YADIF), single rate", rows);
        Assert.Equal(4, rows.Count);

        // With the server on YADIF single rate, double rate off measured with BWDIF isn't the server's choice.
        var onYadif = SpeedAdvisor.Advise(runs[3], runs, HwType.none, string.Empty, new SpeedSettings { Bwdif = false, DoubleRate = false });
        Assert.All(onYadif.Where(s => s.Group == "deinterlace" && s.Current), s => Assert.Equal("Yet Another DeInterlacing Filter (YADIF), single rate", s.Row));
    }

    /// <summary>A codec-scoped setting is compared on a library video too, whose test key the catalog doesn't resolve.</summary>
    [Fact]
    public void ComparesCrfOnALibraryVideo()
    {
        const string Library = "library|h264-8mbps";
        var crf23 = Run(new SpeedSettings { H264Crf = 23 }, Result(HwType.none, Library, 30) with { Command = "23" });
        var crf28 = Run(new SpeedSettings { H264Crf = 28 }, Result(HwType.none, Library, 45) with { Command = "28" });

        var faster = Assert.Single(SpeedAdvisor.Advise(crf28, [crf23, crf28], HwType.none, string.Empty, new SpeedSettings { H264Crf = 23 }), s => s.Setting == "H264Crf" && s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal("28", faster.Value);
    }

    /// <summary>Tone-mapping suggestions name the method each side's runs used, from the catalog's tone mapping group, so the page shows them as one table.</summary>
    [Fact]
    public void NamesTheToneMappingMethod()
    {
        const string Film = "live-action|h264-8mbps";
        var general = Run(new SpeedSettings { Tonemap = true }, Result(HwType.vaapi, Film, 150) with { Command = "general" });
        var vpp = Run(new SpeedSettings { Tonemap = true, VppTonemap = true }, Result(HwType.vaapi, Film, 100) with { Command = "vpp" });
        var off = Run(new SpeedSettings { Tonemap = false }, Result(HwType.vaapi, Film, 100) with { Command = "off" });

        var advice = SpeedAdvisor.Advise(general, [general, vpp, off], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings { Tonemap = true });

        var method = Assert.Single(advice, s => s.Setting == "VppTonemap");
        Assert.Equal(("tonemap", "Tone mapping", "VPP"), (method.Group, method.Row, Assert.Single(method.Compared).Row));
        Assert.Equal("Off", Assert.Single(Assert.Single(advice, s => s.Setting == "Tonemap").Compared).Row);
    }

    /// <summary>When higher H.264 qualities fall behind, no Internet streaming bitrate limit is suggested at the slowest speed, beside the highest quality that keeps up as the limit.</summary>
    [Fact]
    public void SuggestsABitrateLimit()
    {
        var run = Run(new SpeedSettings(), Result(HwType.none, "drama|h264-40mbps", 20), Result(HwType.none, "drama|h264-20mbps", 30), Result(HwType.none, "drama|h264-8mbps", 60));
        var fine = Run(new SpeedSettings(), Result(HwType.none, "drama|h264-40mbps", 30), Result(HwType.none, "drama|h264-8mbps", 60));

        var none = Assert.Single(SpeedAdvisor.Advise(run, [run], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.BitrateLimit);
        Assert.Equal((SpeedAdvisor.BitrateLimitKey, "0", false, 20.0 / 25), (none.Setting, none.Value, none.LowerQuality, none.Speed));
        var limit = Assert.Single(SpeedAdvisor.Advise(run, [run], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.Compatible);
        Assert.Equal((SpeedAdvisor.BitrateLimitKey, "20000000", true), (limit.Setting, limit.Value, limit.LowerQuality));
        Assert.DoesNotContain(SpeedAdvisor.Advise(fine, [fine], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.BitrateLimit);
    }

    /// <summary>Double-rate deinterlacing is compared by speed against its doubled real time, not by its doubled frame count.</summary>
    [Fact]
    public void ComparesDoubleRateBySpeed()
    {
        const string Film = "sports-576i|h264-8mbps";
        var single = Run(new SpeedSettings { Bwdif = true }, Result(HwType.none, Film, 50) with { Command = "single" });
        var doubled = Run(new SpeedSettings { Bwdif = true, DoubleRate = true }, Result(HwType.none, Film, 80) with { Command = "double", FrameRate = 50 });

        var advice = SpeedAdvisor.Advise(doubled, [single, doubled], HwType.none, string.Empty, new SpeedSettings { Bwdif = true });

        Assert.DoesNotContain(advice, s => s.Setting == "DoubleRate" && s.Value == "true" && s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal("false", Assert.Single(advice, s => s.Setting == "DoubleRate" && s.Kind == SpeedSuggestionKind.FasterSetting).Value);
    }

    /// <summary>With double rate off and faster, turning it on is still suggested as better quality while it keeps up and most concurrent streams.</summary>
    [Fact]
    public void SuggestsDoubleRateAsBetterQuality()
    {
        const string Film = "sports-576i|h264-8mbps";
        var single = Run(new SpeedSettings { Bwdif = true }, Result(HwType.none, Film, 172) with { Command = "single", Streams = 16 });
        var doubled = Run(new SpeedSettings { Bwdif = true, DoubleRate = true }, Result(HwType.none, Film, 200) with { Command = "double", FrameRate = 50, Streams = 10 });

        var advice = SpeedAdvisor.Advise(single, [single, doubled], HwType.none, string.Empty, new SpeedSettings { Bwdif = true });

        var faster = Assert.Single(advice, s => s.Setting == "DoubleRate" && s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal(("false", true, true), (faster.Value, faster.Current, faster.LowerQuality));
        var quality = Assert.Single(advice, s => s.Setting == "DoubleRate" && s.Kind == SpeedSuggestionKind.HigherQuality);
        Assert.Equal(("true", 10, 16), (quality.Value, quality.Streams, quality.OtherStreams));
    }

    /// <summary>Of several better-quality presets that keep up, only the best is suggested, with the others in its table.</summary>
    [Fact]
    public void SuggestsOnlyTheBestQuality()
    {
        const string Film = "live-action|h264-8mbps";
        var veryfast = Run(new SpeedSettings { EncoderPreset = "veryfast" }, Result(HwType.none, Film, 1000) with { Command = "veryfast" });
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 900) with { Command = "fast" });
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 800) with { Command = "medium" });

        var quality = Assert.Single(SpeedAdvisor.Advise(veryfast, [veryfast, fast, medium], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "veryfast" }), s => s.Kind == SpeedSuggestionKind.HigherQuality);
        Assert.Equal("medium", quality.Value);
        Assert.Equal([("veryfast", true, true), ("fast", false, true)], quality.Compared.Select(c => (c.Value, c.Current, c.LowerQuality == true)));
    }

    /// <summary>Auto is the encoding preset recommended whenever it was measured, with every other preset compared listed beside it and the slowest that keeps up named: on the Intel NAS, faster measured faster than Auto on HEVC but slower on H.264.</summary>
    [Fact]
    public void AlwaysRecommendsAuto()
    {
        const string H264 = "live-action|h264-8mbps";
        const string Hevc = "live-action|hevc-8mbps";
        SpeedReport Preset(string preset, double h264, int h264Streams, double hevc, int hevcStreams) => Run(
            new SpeedSettings { EncoderPreset = preset },
            Result(HwType.qsv, H264, h264 * 25) with { Command = preset + "-h264", Streams = h264Streams },
            Result(HwType.qsv, Hevc, hevc * 25) with { Command = preset + "-hevc", Streams = hevcStreams });
        SpeedReport[] runs = [Preset("auto", 8.3, 10, 3.97, 4), Preset("faster", 6.9, 9, 4.47, 5), Preset("fast", 5.5, 6, 1.07, 1), Preset("medium", 5.3, 7, 1.19, 1), Preset("slow", 5.2, 7, 1.16, 1)];

        var onAuto = SpeedAdvisor.Advise(runs[0], runs, HwType.qsv, "/dev/dri/renderD128", new SpeedSettings());
        var onFaster = SpeedAdvisor.Advise(runs[0], runs, HwType.qsv, "/dev/dri/renderD128", new SpeedSettings { EncoderPreset = "faster" });

        var preset = Assert.Single(onAuto, s => s.Setting == "EncoderPreset");
        Assert.Equal((SpeedSuggestionKind.RecommendedValue, "auto", true, "faster"), (preset.Kind, preset.Value, preset.Current, preset.BestQuality));
        Assert.Equal(["fast", "faster", "medium", "slow"], preset.Compared.Select(c => c.Value).Order(StringComparer.Ordinal));
        Assert.All(preset.Compared, c => Assert.Equal([H264, Hevc], c.Speeds.Select(o => o.Label)));
        Assert.Equal((4.47, 5), (Math.Round(preset.Compared.Single(c => c.Value == "faster").Speeds[1].Speed, 2), preset.Compared.Single(c => c.Value == "faster").Speeds[1].Streams));

        var switchBack = Assert.Single(onFaster, s => s.Setting == "EncoderPreset");
        Assert.Equal((SpeedSuggestionKind.RecommendedValue, "auto", false, "faster"), (switchBack.Kind, switchBack.Value, switchBack.Current, switchBack.BestQuality));
        Assert.True(switchBack.Compared.Single(c => c.Value == "faster").Current);

        // VAAPI leaves Auto to the driver, so no preset is better or worse than it there.
        var onVaapi = Assert.Single(SpeedAdvisor.Advise(runs[0], runs, HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings()), s => s.Setting == "EncoderPreset");
        Assert.All(onVaapi.Compared, c => Assert.Null(c.LowerQuality));
        Assert.Null(onVaapi.BestQuality);
    }

    /// <summary>A value only as fast as the server's within noise isn't recommended for using less when the server's value measured faster than it by more.</summary>
    [Fact]
    public void KeepsAServerValueThatMeasuredFaster()
    {
        const string Film = "live-action|h264-8mbps";
        static SpeedResult Used(SpeedResult r, double cpuSeconds) => r with { Resources = new Core.Resources.ResourceUsage(10, cpuSeconds, 400_000_000) };
        var four = Run(new SpeedSettings { EncodingThreadCount = 4 }, Used(Result(HwType.none, Film, 2.102 * 25), 40) with { Command = "four" });
        var two = Run(new SpeedSettings { EncodingThreadCount = 2 }, Used(Result(HwType.none, Film, 2.0 * 25), 20) with { Command = "two" });

        var kept = Assert.Single(SpeedAdvisor.Advise(two, [four, two], HwType.none, string.Empty, new SpeedSettings { EncodingThreadCount = 4 }), s => s.Setting == "EncodingThreadCount");

        Assert.Equal((SpeedSuggestionKind.FasterSetting, "4", true), (kept.Kind, kept.Value, kept.Current));
        Assert.Equal(2.102, Assert.NotNull(kept.Speed), 3);
    }

    /// <summary>A better-quality value that measured faster than the server's contradicts the quality order, so it isn't recommended, but its table still lists it.</summary>
    [Fact]
    public void DropsABetterQualityValueThatMeasuredFaster()
    {
        const string Film = "live-action|h264-8mbps";
        var crf28 = Run(new SpeedSettings { H264Crf = 28 }, Result(HwType.none, Film, 100) with { Command = "28" });
        var crf23 = Run(new SpeedSettings { H264Crf = 23 }, Result(HwType.none, Film, 120) with { Command = "23" });

        var kept = Assert.Single(SpeedAdvisor.Advise(crf23, [crf28, crf23], HwType.none, string.Empty, new SpeedSettings { H264Crf = 28 }), s => s.Setting == "H264Crf");

        Assert.Equal((SpeedSuggestionKind.NoChange, "28", true), (kept.Kind, kept.Value, kept.Current));
        Assert.Equal(("23", false), (Assert.Single(kept.Compared).Value, kept.Compared[0].LowerQuality));
    }

    /// <summary>Several faster values make one table, recommending the fastest, and the gain its reason gives is the smallest any output measured: with one run per value, the ratio of the speeds the table shows.</summary>
    [Fact]
    public void TakesTheGainFromTheRows()
    {
        const string Film = "live-action|h264-8mbps";
        const string Pattern = "pattern|h264-8mbps";
        SpeedReport Crf(int crf, double film, double pattern) => Run(new SpeedSettings { H264Crf = crf }, Result(HwType.none, Film, film) with { Command = $"{crf}-film" }, Result(HwType.none, Pattern, pattern) with { Command = $"{crf}-pattern" });
        SpeedReport[] runs = [Crf(18, 30, 90), Crf(23, 36, 135), Crf(28, 45, 180)];

        var faster = Assert.Single(SpeedAdvisor.Advise(runs[0], runs, HwType.none, string.Empty, new SpeedSettings { H264Crf = 18 }), s => s.Setting == "H264Crf");

        Assert.Equal((SpeedSuggestionKind.FasterSetting, "28", 0.5), (faster.Kind, faster.Value, Math.Round(Assert.NotNull(faster.Gain), 2)));
        Assert.Equal([("18", true), ("23", false)], faster.Compared.Select(c => (c.Value, c.Current)));
    }

    /// <summary>Outputs below real time on the configured backend are flagged, unless every backend measured fell behind too, and marked when only test videos showed it.</summary>
    [Fact]
    public void FlagsOutputsThatFallBehind()
    {
        var run = Run(new SpeedSettings(), Result(HwType.qsv, "pattern|h264-8mbps", 20), Result(HwType.none, "pattern|h264-8mbps", 50), Result(HwType.qsv, "pattern|hevc-8mbps", 20), Result(HwType.none, "pattern|hevc-8mbps", 10), Result(HwType.qsv, "pattern|av1-8mbps", 50), Result(HwType.qsv, "pattern|vp9-8mbps", 20));

        var behind = Assert.Single(SpeedAdvisor.Advise(run, [run], HwType.qsv, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FallsBehind);

        Assert.Equal(["pattern|h264-8mbps", "pattern|vp9-8mbps"], behind.Outputs);
        Assert.Equal([("pattern|h264-8mbps", 20.0 / 25), ("pattern|vp9-8mbps", 20.0 / 25)], behind.Speeds.Select(o => (o.Label, o.Speed)));
        Assert.True(behind.TestVideosOnly);
    }

    /// <summary>Images below real time aren't reported: they're extracted ahead of playback.</summary>
    [Fact]
    public void ImagesHaveNoRealTime()
    {
        var run = Run(new SpeedSettings(), Result(HwType.qsv, "pattern|trickplay", 10) with { Kind = SpeedOutputKind.Images }, Result(HwType.none, "pattern|trickplay", 5) with { Kind = SpeedOutputKind.Images });

        Assert.DoesNotContain(SpeedAdvisor.Advise(run, [run], HwType.qsv, string.Empty, new SpeedSettings()), s => s.Kind is SpeedSuggestionKind.FallsBehind or SpeedSuggestionKind.TooSlowEverywhere);
    }

    /// <summary>An output none of several backends keeps real time on is reported once, with the fastest backend; one measured on a single backend isn't.</summary>
    [Fact]
    public void ReportsOutputsTooSlowOnEveryBackend()
    {
        var run = Run(new SpeedSettings(), Result(HwType.qsv, "pattern|h264-8mbps", 20), Result(HwType.none, "pattern|h264-8mbps", 10), Result(HwType.qsv, "pattern|hevc-8mbps", 20), Result(HwType.none, "pattern|hevc-8mbps", 50), Result(HwType.qsv, "pattern|av1-8mbps", 20));

        var slow = Assert.Single(SpeedAdvisor.Advise(run, [run], HwType.qsv, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.TooSlowEverywhere);

        Assert.Equal(["pattern|h264-8mbps"], slow.Outputs);
        Assert.Equal(HwType.qsv, slow.Type);
    }

    /// <summary>Runs differing in one setting suggest its faster value, or its better-quality value when that still keeps up.</summary>
    [Fact]
    public void ComparesRunsThatDifferInOneSetting()
    {
        const string Film = "live-action|h264-8mbps";
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 125), Result(HwType.none, "pattern|h264-8mbps", 375));
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 150), Result(HwType.none, "pattern|h264-8mbps", 450));
        var nvdec = Run(new SpeedSettings { EncoderPreset = "fast", EnhancedNvdec = false }, Result(HwType.none, Film, 150));
        var slowMedium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 30), Result(HwType.none, "pattern|h264-8mbps", 90));
        var slowFast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 36), Result(HwType.none, "pattern|h264-8mbps", 108));

        // Medium below the headroom a better-quality value needs, so fast is recommended for its speed.
        var onMedium = SpeedAdvisor.Advise(slowFast, [slowMedium, slowFast], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "medium" });
        var onFast = SpeedAdvisor.Advise(fast, [medium, fast, nvdec], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "fast" });

        var faster = Assert.Single(onMedium, s => s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal(("EncoderPreset", "fast", "medium", 0.2, false), (faster.Setting, faster.Value, Assert.Single(faster.Others), Math.Round(Assert.NotNull(faster.Gain), 2), faster.TestVideosOnly));

        // Headroom comes from the film alone: the test video's 12x overstates it.
        var quality = Assert.Single(onFast, s => s.Kind == SpeedSuggestionKind.HigherQuality);
        Assert.Equal(("medium", 5.0), (quality.Value, Assert.NotNull(quality.Speed)));
        Assert.Equal([Film], quality.Outputs);
        Assert.Equal(SpeedSuggestionKind.NoChange, Assert.Single(onFast, s => s.Setting == "EnhancedNvdec").Kind);
    }

    /// <summary>Several runs that suggest the same value become one suggestion, and only comparisons with the server's value count.</summary>
    [Fact]
    public void MergesTheSameSuggestion()
    {
        const string Film = "live-action|h264-8mbps";
        const string Anime = "anime|h264-8mbps";
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 100));
        var mediumAnime = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Anime, 100));
        var alike = 100 * (1 + (_advice.Noise / 4));
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, alike), Result(HwType.none, Anime, alike));
        var faster = Run(new SpeedSettings { EncoderPreset = "faster" }, Result(HwType.none, Film, 100 * (1 + (_advice.Noise / 2))));

        var advice = SpeedAdvisor.Advise(fast, [medium, mediumAnime, fast, faster], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "fast" });

        var quality = Assert.Single(advice, s => s.Value == "medium");
        Assert.Equal(SpeedSuggestionKind.HigherQuality, quality.Kind);
        Assert.Equal(["fast"], quality.Others);
        Assert.Equal([Anime, Film], quality.Outputs.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(advice, s => s.Value == "faster");
    }

    /// <summary>A faster value that turns tone mapping off is never suggested, and a faster preset is marked lower quality.</summary>
    [Fact]
    public void GuardsThePicture()
    {
        const string Film = "live-action|h264-8mbps";
        var on = Run(new SpeedSettings { Tonemap = true }, Result(HwType.none, Film, 100));
        var off = Run(new SpeedSettings { Tonemap = false }, Result(HwType.none, Film, 200));
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 100));
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 150));

        Assert.Equal(SpeedSuggestionKind.NoChange, Assert.Single(SpeedAdvisor.Advise(on, [on, off], HwType.none, string.Empty, new SpeedSettings { Tonemap = true }), s => s.Setting == "Tonemap").Kind);
        Assert.True(Assert.Single(SpeedAdvisor.Advise(medium, [medium, fast], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "medium" }), s => s.Setting == "EncoderPreset").Compared.Single(c => c.Value == "fast").LowerQuality);
    }

    /// <summary>Returns a run.</summary>
    /// <param name="settings">Its settings.</param>
    /// <param name="results">Its results.</param>
    /// <returns>The run.</returns>
    private static SpeedReport Run(SpeedSettings settings, params SpeedResult[] results) =>
        new(_time, new FfmpegSummary("/usr/bin/ffmpeg", "Server", "7.1", true), SpeedMethod.Quick, results) { Settings = settings };

    /// <summary>Returns a measured result at 25 fps source.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="test">The test, also its label.</param>
    /// <param name="fps">Its fps.</param>
    /// <returns>The result.</returns>
    private static SpeedResult Result(HwType type, string test, double fps) =>
        new(type, type == HwType.none ? string.Empty : "/dev/dri/renderD128", test, string.Empty, fps, null, false, null) { FrameRate = 25, Label = test };
}
