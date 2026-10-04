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
        var alike = Run(new SpeedSettings(), Result(HwType.vaapi, "a", 100), Result(HwType.qsv, "a", 97));
        var vaapiFaster = Run(new SpeedSettings(), Result(HwType.vaapi, "a", 100), Result(HwType.qsv, "a", 80));

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
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.qsv, Film, 100) with { Command = "medium" });
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.qsv, Film, 150) with { Command = "fast" });

        var faster = Assert.Single(SpeedAdvisor.Advise(fast, [medium, fast], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings { EncoderPreset = "medium" }), s => s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal(("EncoderPreset", "fast"), (faster.Setting, faster.Value));
    }

    /// <summary>A backend that measures alike with the configured one is suggested when it used less, with what it saved; a setting value likewise.</summary>
    [Fact]
    public void PrefersWhatUsesLess()
    {
        const string Film = "live-action|h264-8mbps";
        static SpeedResult Used(SpeedResult r, double cpuSeconds, long memory) => r with { Resources = new Core.Resources.ResourceUsage(10, cpuSeconds, memory) };
        var run = Run(new SpeedSettings(), Used(Result(HwType.vaapi, Film, 100), 8, 400_000_000), Used(Result(HwType.nvenc, Film, 102), 4, 400_000_000) with { Device = string.Empty });

        var backend = Assert.Single(SpeedAdvisor.Advise(run, [run], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FastestBackend);
        Assert.Equal((HwType.nvenc, false), (backend.Type, backend.Preferred));
        Assert.Equal([("Cpu", 0.5)], backend.Savings.Select(x => (x.Resource, Math.Round(x.Fraction, 2))));

        // Using more memory cancels the saving.
        var mixed = Run(new SpeedSettings(), Used(Result(HwType.vaapi, Film, 100), 8, 400_000_000), Used(Result(HwType.nvenc, Film, 102), 4, 800_000_000) with { Device = string.Empty });
        Assert.DoesNotContain(SpeedAdvisor.Advise(mixed, [mixed], HwType.vaapi, "/dev/dri/renderD128", new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FastestBackend);

        var auto = Run(new SpeedSettings(), Used(Result(HwType.none, Film, 100), 40, 600_000_000) with { Command = "auto" });
        var four = Run(new SpeedSettings { EncodingThreadCount = 4 }, Used(Result(HwType.none, Film, 98), 30, 500_000_000) with { Command = "four" });
        var efficient = Assert.Single(SpeedAdvisor.Advise(four, [auto, four], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.EfficientSetting);
        Assert.Equal(("EncodingThreadCount", "4"), (efficient.Setting, efficient.Value));
        Assert.Equal(["Cpu", "Memory"], efficient.Savings.Select(x => x.Resource));
    }

    /// <summary>VBR audio is suggested as the better-quality value when it still keeps up.</summary>
    [Fact]
    public void SuggestsVbrAudio()
    {
        const string Film = "live-action|h264-8mbps";
        var off = Run(new SpeedSettings(), Result(HwType.none, Film, 430) with { Command = "cbr" });
        var on = Run(new SpeedSettings { AudioVbr = true }, Result(HwType.none, Film, 425) with { Command = "vbr" });

        var quality = Assert.Single(SpeedAdvisor.Advise(on, [off, on], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.HigherQuality);
        Assert.Equal(("AudioVbr", "true"), (quality.Setting, quality.Value));
    }

    /// <summary>A server value that wins is reported as current, and a better-quality value that costs many concurrent streams isn't suggested.</summary>
    [Fact]
    public void ReportsTheCurrentValueAndGuardsStreams()
    {
        const string Film = "live-action|h264-8mbps";
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 150) with { Command = "fast", Streams = 12 });
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 100) with { Command = "medium", Streams = 6 });

        var advice = SpeedAdvisor.Advise(fast, [fast, medium], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "fast" });

        var current = Assert.Single(advice, s => s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal(("fast", true, 12, 6), (current.Value, current.Current, current.Streams, current.OtherStreams));
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

    /// <summary>When higher H.264 qualities fall behind, the highest that keeps up is suggested as the Internet streaming bitrate limit.</summary>
    [Fact]
    public void SuggestsABitrateLimit()
    {
        var run = Run(new SpeedSettings(), Result(HwType.none, "drama|h264-40mbps", 20), Result(HwType.none, "drama|h264-20mbps", 30), Result(HwType.none, "drama|h264-8mbps", 60));
        var fine = Run(new SpeedSettings(), Result(HwType.none, "drama|h264-40mbps", 30), Result(HwType.none, "drama|h264-8mbps", 60));

        var limit = Assert.Single(SpeedAdvisor.Advise(run, [run], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.BitrateLimit);
        Assert.Equal(("RemoteClientBitrateLimit", "20000000"), (limit.Setting, limit.Value));
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

    /// <summary>Of several better-quality presets that keep up, only the best is suggested.</summary>
    [Fact]
    public void SuggestsOnlyTheBestQuality()
    {
        const string Film = "live-action|h264-8mbps";
        var auto = Run(new SpeedSettings(), Result(HwType.none, Film, 1000) with { Command = "auto" });
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 900) with { Command = "fast" });
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 800) with { Command = "medium" });

        var quality = Assert.Single(SpeedAdvisor.Advise(auto, [auto, fast, medium], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.HigherQuality);
        Assert.Equal("medium", quality.Value);
    }

    /// <summary>Outputs below real time on the configured backend are flagged, and marked when only test videos showed it.</summary>
    [Fact]
    public void FlagsOutputsThatFallBehind()
    {
        var run = Run(new SpeedSettings(), Result(HwType.none, "pattern|h264-8mbps", 20), Result(HwType.none, "pattern|hevc-8mbps", 50));

        var behind = Assert.Single(SpeedAdvisor.Advise(run, [run], HwType.none, string.Empty, new SpeedSettings()), s => s.Kind == SpeedSuggestionKind.FallsBehind);

        Assert.Equal(["pattern|h264-8mbps"], behind.Outputs);
        Assert.True(behind.TestVideosOnly);
    }

    /// <summary>Runs differing in one setting suggest its faster value, or its better-quality value when that still keeps up.</summary>
    [Fact]
    public void ComparesRunsThatDifferInOneSetting()
    {
        const string Film = "live-action|h264-8mbps";
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 125), Result(HwType.none, "pattern|h264-8mbps", 375));
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 150), Result(HwType.none, "pattern|h264-8mbps", 450));
        var threads = Run(new SpeedSettings { EncoderPreset = "fast", EncodingThreadCount = 4 }, Result(HwType.none, Film, 150));

        var onMedium = SpeedAdvisor.Advise(fast, [medium, fast, threads], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "medium" });
        var onFast = SpeedAdvisor.Advise(fast, [medium, fast, threads], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "fast" });

        var faster = Assert.Single(onMedium, s => s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal(("EncoderPreset", "fast", "medium", 0.2, false), (faster.Setting, faster.Value, Assert.Single(faster.Others), Math.Round(faster.Gain!.Value, 2), faster.TestVideosOnly));

        // Headroom comes from the film alone: the test video's 12x overstates it.
        var quality = Assert.Single(onFast, s => s.Kind == SpeedSuggestionKind.HigherQuality);
        Assert.Equal(("medium", 5.0), (quality.Value, quality.Speed!.Value));
        Assert.Equal([Film], quality.Outputs);
        Assert.Equal(SpeedSuggestionKind.NoChange, Assert.Single(onFast, s => s.Setting == "EncodingThreadCount").Kind);
    }

    /// <summary>Several runs that suggest the same value become one suggestion, and only comparisons with the server's value count.</summary>
    [Fact]
    public void MergesTheSameSuggestion()
    {
        const string Film = "live-action|h264-8mbps";
        const string Anime = "anime|h264-8mbps";
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 100));
        var mediumAnime = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Anime, 100));
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 101), Result(HwType.none, Anime, 101));
        var faster = Run(new SpeedSettings { EncoderPreset = "faster" }, Result(HwType.none, Film, 102));

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
        Assert.True(Assert.Single(SpeedAdvisor.Advise(medium, [medium, fast], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "medium" }), s => s.Kind == SpeedSuggestionKind.FasterSetting).LowerQuality);
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
