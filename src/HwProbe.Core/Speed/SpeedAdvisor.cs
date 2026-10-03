using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Draws suggestions from measured results: a faster hardware backend, outputs that fall behind, and settings worth changing.</summary>
/// <remarks>Software is never suggested over hardware: a GPU encoder draws less power for the same work.</remarks>
public static class SpeedAdvisor
{
    /// <summary>Differences smaller than this are run-to-run noise, as the page treats them.</summary>
    public const double Noise = 0.05;

    /// <summary>A higher-quality value is suggested only when it keeps at least this multiple of real time, leaving room for a second stream or a busy server.</summary>
    public const double Headroom = 1.5;

    /// <summary>Settings where a slower value gives a better picture, with their values from best quality to fastest.</summary>
    private static readonly Dictionary<string, string[]> _quality = new(StringComparer.Ordinal)
    {
        ["EncoderPreset"] = ["veryslow", "slower", "slow", "medium", "fast", "faster", "veryfast", "superfast", "ultrafast"],
        ["H264Crf"] = [.. Enumerable.Range(0, 52).Select(n => n.ToString(CultureInfo.InvariantCulture))],
        ["H265Crf"] = [.. Enumerable.Range(0, 52).Select(n => n.ToString(CultureInfo.InvariantCulture))],
    };

    /// <summary>The settings a run records, by catalog option key, with the value it used; Audio and BurnIn describe the client, not the server, so they're left out.</summary>
    private static readonly Dictionary<string, Func<SpeedSettings, string>> _values = new(StringComparer.Ordinal)
    {
        ["QsvLowPowerH264"] = s => Flag(s.QsvLowPowerH264 ?? s.LowPowerH264),
        ["QsvLowPowerHevc"] = s => Flag(s.QsvLowPowerHevc ?? s.LowPowerHevc),
        ["EnhancedNvdec"] = s => Flag(s.EnhancedNvdec),
        ["PreferNativeDecoder"] = s => Flag(s.PreferNativeDecoder),
        ["VppTonemap"] = s => Flag(s.VppTonemap),
        ["VideoToolboxTonemap"] = s => Flag(s.VideoToolboxTonemap),
        ["Tonemap"] = s => Flag(s.Tonemap),
        ["TonemapAlgorithm"] = s => s.TonemapAlgorithm,
        ["TonemapMode"] = s => s.TonemapMode,
        ["TonemapRange"] = s => s.TonemapRange,
        ["TonemapDesat"] = s => Number(s.TonemapDesat),
        ["TonemapPeak"] = s => Number(s.TonemapPeak),
        ["TonemapParam"] = s => Number(s.TonemapParam),
        ["EncodingThreadCount"] = s => s.EncodingThreadCount.ToString(CultureInfo.InvariantCulture),
        ["AudioVbr"] = s => Flag(s.AudioVbr),
        ["DownmixBoost"] = s => Number(s.DownmixBoost),
        ["DownmixAlgorithm"] = s => s.DownmixAlgorithm,
        ["EncoderPreset"] = s => s.EncoderPreset ?? "auto",
        ["H265Crf"] = s => s.H265Crf.ToString(CultureInfo.InvariantCulture),
        ["H264Crf"] = s => s.H264Crf.ToString(CultureInfo.InvariantCulture),
        ["DeinterlaceMethod"] = s => s.Bwdif ? "bwdif" : "yadif",
        ["DoubleRate"] = s => Flag(s.DoubleRate),
    };

    /// <summary>Gets the catalog option keys a suggestion can set.</summary>
    public static IReadOnlyCollection<string> Settings => _values.Keys;

    /// <summary>Returns the value a setting has in some settings, as the catalog keys it.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">A key from <see cref="Settings"/>.</param>
    /// <returns>The value.</returns>
    public static string ValueOf(SpeedSettings settings, string key) => _values[key](settings);

    /// <summary>Draws the suggestions.</summary>
    /// <param name="shown">The run shown, for the backend suggestions.</param>
    /// <param name="runs">Every saved run, the shown one included, for the setting comparisons.</param>
    /// <param name="type">The configured backend; <see cref="HwType.none"/> for software.</param>
    /// <param name="device">Its device, or empty.</param>
    /// <param name="server">The server's settings now, so a value already set isn't suggested.</param>
    /// <returns>The suggestions, backend ones first.</returns>
    public static IReadOnlyList<SpeedSuggestion> Advise(SpeedReport shown, IReadOnlyList<SpeedReport> runs, HwType type, string device, SpeedSettings server)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(server);

        // Jellyfin keeps a device only for VAAPI and QSV; the others match any device.
        bool Configured(SpeedResult r) => r.Type == type && (string.IsNullOrEmpty(device) || r.Device == device);
        List<SpeedSuggestion> suggestions = [];

        var measured = shown.Results.Where(r => r.Fps is > 0 && string.IsNullOrEmpty(r.Variant) && !r.Pending).ToList();
        var hardware = measured.Where(r => r.Type != HwType.none).ToList();
        if (hardware.Count > 0)
        {
            var winners = hardware.GroupBy(r => r.Test, StringComparer.Ordinal).Select(g => g.Aggregate(Better)).ToList();
            var best = winners.GroupBy(r => (r.Type, r.Device)).OrderByDescending(g => g.Count()).First();

            // On software, any working hardware backend is suggested, however it compares: it draws less power for the same work.
            List<SpeedResult> beaten = type == HwType.none ? [.. best] : [.. best.Where(w => measured.FirstOrDefault(r => r.Test == w.Test && Configured(r)) is not { } mine || Gain(w, mine) > Noise)];
            if ((best.Key.Type != type || (!string.IsNullOrEmpty(device) && best.Key.Device != device)) && beaten.Count > 0)
            {
                suggestions.Add(new SpeedSuggestion(SpeedSuggestionKind.FastestBackend, [.. beaten.Select(Label)]) { Type = best.Key.Type, Device = best.Key.Device, TestVideosOnly = beaten.All(IsGenerated) });
            }
        }

        // A test video that falls behind means real video will too: test videos encode faster.
        var behind = measured.Where(r => Configured(r) && (Speed(r) < 1 || r.Streams == 0)).ToList();
        if (behind.Count > 0)
        {
            suggestions.Add(new SpeedSuggestion(SpeedSuggestionKind.FallsBehind, [.. behind.Select(Label)]) { Type = type, Device = device, TestVideosOnly = behind.All(IsGenerated) });
        }

        suggestions.AddRange(CompareSettings(runs, Configured, server));
        return suggestions;
    }

    /// <summary>Compares runs that differ in one setting, on the configured backend's results.</summary>
    /// <param name="runs">Every saved run.</param>
    /// <param name="configured">Whether a result is the configured backend's.</param>
    /// <param name="server">The server's settings now.</param>
    /// <returns>The setting suggestions.</returns>
    private static IEnumerable<SpeedSuggestion> CompareSettings(IReadOnlyList<SpeedReport> runs, Func<SpeedResult, bool> configured, SpeedSettings server)
    {
        // Per setting and value: each output's speed, from every run that differs from another in that setting alone.
        List<(string Key, string Value, string Other, string Test, string Label, double Gain, double Speed, bool Generated)> seen = [];
        var withSettings = runs.Where(r => r.Settings is not null).ToList();
        for (var i = 0; i < withSettings.Count; i++)
        {
            for (var j = 0; j < withSettings.Count; j++)
            {
                var (a, b) = (withSettings[i], withSettings[j]);
                if (i == j || a.Method != b.Method || a.Repeats != b.Repeats)
                {
                    continue;
                }

                var differs = _values.Where(v => v.Value(a.Settings!) != v.Value(b.Settings!)).Select(v => v.Key).ToList();
                if (differs.Count != 1)
                {
                    continue;
                }

                var key = differs[0];
                foreach (var mine in a.Results.Where(r => r.Fps is > 0 && string.IsNullOrEmpty(r.Variant) && configured(r)))
                {
                    // The same command means the setting doesn't reach this output (CRF on a hardware encoder, presets VideoToolbox maps alike).
                    if (b.Results.FirstOrDefault(r => r.Test == mine.Test && r.Fps is > 0 && string.IsNullOrEmpty(r.Variant) && configured(r)) is { } theirs
                        && (mine.Command is null || mine.Command != theirs.Command))
                    {
                        seen.Add((key, _values[key](a.Settings!), _values[key](b.Settings!), mine.Test, Label(mine), Gain(mine, theirs), Speed(mine), IsGenerated(mine)));
                    }
                }
            }
        }

        foreach (var group in seen.GroupBy(s => (s.Key, s.Value, s.Other)))
        {
            var (key, value, other) = group.Key;
            if (value == _values[key](server))
            {
                continue;
            }

            var gains = group.Select(s => s.Gain).Order().ToList();
            var median = gains[gains.Count / 2];
            var outputs = group.Select(s => s.Label).Distinct(StringComparer.Ordinal).ToList();
            var generated = group.All(s => s.Generated);
            if (gains.All(g => g > Noise))
            {
                yield return new SpeedSuggestion(SpeedSuggestionKind.FasterSetting, outputs) { Setting = key, Value = value, Other = other, Gain = median, Speed = group.Min(s => s.Speed), TestVideosOnly = generated };
                continue;
            }

            // Headroom is judged on real video only: a test video's speed overstates it.
            var real = group.Where(s => !s.Generated).ToList();
            if (IsBetterQuality(key, value, other) && real.Count > 0 && real.Min(s => s.Speed) >= Headroom)
            {
                yield return new SpeedSuggestion(SpeedSuggestionKind.HigherQuality, [.. real.Select(s => s.Label).Distinct(StringComparer.Ordinal)]) { Setting = key, Value = value, Other = other, Gain = median, Speed = real.Min(s => s.Speed) };
            }
        }
    }

    /// <summary>Reports whether a value gives a better picture than another.</summary>
    /// <param name="key">The setting.</param>
    /// <param name="value">The value.</param>
    /// <param name="other">The other value.</param>
    /// <returns>True when the setting trades speed for quality and the value is the better-quality one.</returns>
    private static bool IsBetterQuality(string key, string value, string other)
    {
        // Auto is veryfast for VOD (DynamicHlsController.DefaultVodEncoderPreset, v12.1).
        static string Preset(string v) => v == "auto" ? "veryfast" : v;
        return _quality.TryGetValue(key, out var order)
            && Array.IndexOf(order, Preset(value)) is var mine and >= 0
            && Array.IndexOf(order, Preset(other)) is var theirs and >= 0
            && mine < theirs;
    }

    /// <summary>Returns the better of two results for one output: more streams kept up, then faster.</summary>
    /// <param name="a">One result.</param>
    /// <param name="b">The other.</param>
    /// <returns>The better one.</returns>
    private static SpeedResult Better(SpeedResult a, SpeedResult b) =>
        (b.Streams ?? -1) > (a.Streams ?? -1) || ((b.Streams ?? -1) == (a.Streams ?? -1) && b.Fps > a.Fps) ? b : a;

    /// <summary>Reports whether a result is from a generated test video rather than a film sample or library file.</summary>
    /// <param name="r">The result.</param>
    /// <returns>True for a generated video.</returns>
    private static bool IsGenerated(SpeedResult r) =>
        SpeedCatalog.FindVideo(r.Test.Split('|')[0]) is { Title: null, File: null };

    /// <summary>Returns how much faster one result is than another, as a fraction.</summary>
    /// <param name="a">The result.</param>
    /// <param name="b">The one it's compared with.</param>
    /// <returns>For example 0.2 for 20% faster.</returns>
    private static double Gain(SpeedResult a, SpeedResult b) => (a.Fps!.Value / b.Fps!.Value) - 1;

    /// <summary>Returns a result's speed as a multiple of real time.</summary>
    /// <param name="r">The result.</param>
    /// <returns>The speed; fps alone when the frame rate wasn't recorded.</returns>
    private static double Speed(SpeedResult r) => r.Fps!.Value / (r.FrameRate is > 0 and var rate ? rate : 1);

    /// <summary>Returns what the results call an output.</summary>
    /// <param name="r">The result.</param>
    /// <returns>For example <c>Test video, H.264: H.264, 8 Mbps</c>.</returns>
    private static string Label(SpeedResult r) => r.Video is not null && r.Output is not null ? r.Video + ": " + r.Output : r.Label ?? r.Test;

    /// <summary>Formats a switch as the catalog keys it.</summary>
    /// <param name="on">The switch.</param>
    /// <returns><c>true</c> or <c>false</c>.</returns>
    private static string Flag(bool on) => on ? "true" : "false";

    /// <summary>Formats a number as the catalog keys it.</summary>
    /// <param name="value">The number.</param>
    /// <returns>For example <c>0.5</c>.</returns>
    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
}
