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

    /// <summary>A faster hardware backend is suggested; software never is, even when it's faster.</summary>
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
        var medium = Run(new SpeedSettings { EncoderPreset = "medium" }, Result(HwType.none, Film, 100), Result(HwType.none, "pattern|h264-8mbps", 300));
        var fast = Run(new SpeedSettings { EncoderPreset = "fast" }, Result(HwType.none, Film, 150), Result(HwType.none, "pattern|h264-8mbps", 450));
        var threads = Run(new SpeedSettings { EncoderPreset = "fast", EncodingThreadCount = 4 }, Result(HwType.none, Film, 150));

        var onMedium = SpeedAdvisor.Advise(fast, [medium, fast, threads], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "medium" });
        var onFast = SpeedAdvisor.Advise(fast, [medium, fast, threads], HwType.none, string.Empty, new SpeedSettings { EncoderPreset = "fast" });

        var faster = Assert.Single(onMedium, s => s.Kind == SpeedSuggestionKind.FasterSetting);
        Assert.Equal(("EncoderPreset", "fast", "medium", 0.5, false), (faster.Setting, faster.Value, faster.Other, faster.Gain!.Value, faster.TestVideosOnly));

        // Headroom comes from the film alone: the test video's 12x overstates it.
        var quality = Assert.Single(onFast, s => s.Kind == SpeedSuggestionKind.HigherQuality);
        Assert.Equal(("medium", 4.0), (quality.Value, quality.Speed!.Value));
        Assert.Equal([Film], quality.Outputs);
        Assert.DoesNotContain(onFast, s => s.Setting == "EncodingThreadCount");
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
