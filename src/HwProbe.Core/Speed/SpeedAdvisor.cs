using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Draws suggestions from measured results: a faster hardware backend, outputs that fall behind, and settings worth changing.</summary>
/// <remarks>Software is never suggested over hardware: a GPU encoder draws less power for the same work.</remarks>
public static class SpeedAdvisor
{
    /// <summary>The setting a bitrate-limit suggestion names: Jellyfin's Internet streaming bitrate limit, in bits per second.</summary>
    public const string BitrateLimitKey = "RemoteClientBitrateLimit";

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

    /// <summary>Gets the share under which two speeds count as alike.</summary>
    public static double Noise => Catalog.Default.Advice.Noise;

    /// <summary>Gets the share of concurrent streams a better-quality value may cost and still be suggested.</summary>
    public static double MaxStreamLoss => Catalog.Default.Advice.MaxStreamLoss;

    /// <summary>Gets the multiple of real time a better-quality value must keep on real video.</summary>
    public static double Headroom => Catalog.Default.Advice.Headroom;

    /// <summary>Gets the catalog option keys a suggestion can set.</summary>
    public static IReadOnlyCollection<string> Settings => _values.Keys;

    /// <summary>Returns the value a setting has in some settings, as the catalog keys it.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">A key from <see cref="Settings"/>.</param>
    /// <returns>The value.</returns>
    public static string ValueOf(SpeedSettings settings, string key) => _values[key](settings);

    /// <summary>Returns the settings to change so the server makes a group's choice: the row's own conditions and the values it applies, and any switch an earlier row that would still take precedence needs turned off.</summary>
    /// <param name="groupKey">The group's key.</param>
    /// <param name="row">The row's label.</param>
    /// <param name="server">The server's settings now.</param>
    /// <param name="type">The configured backend.</param>
    /// <returns>The option keys and values that differ from the server's; null when the row can't be reached, as a row without conditions can't.</returns>
    public static IReadOnlyList<(string Key, string Value)>? GroupRowChanges(string groupKey, string row, SpeedSettings server, HwType type)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (Catalog.Default.SettingGroups.FirstOrDefault(g => g.Key == groupKey) is not { } group
            || group.Rows.FirstOrDefault(r => r.Label == row) is not { When: { } when } target
            || (target.Backends is not null && !target.Backends.Contains(type)))
        {
            return null;
        }

        var changes = new Dictionary<string, string>(when.Concat(target.Applies ?? new Dictionary<string, string>()), StringComparer.Ordinal);
        for (var attempt = 0; attempt < group.Rows.Count; attempt++)
        {
            var trial = changes.Aggregate((SpeedSettings?)server, (s, c) => s is null ? null : SpeedSettingsOptions.Apply(s, c.Key, c.Value));
            var reached = trial is null ? null : RowOf(group, trial, type);
            if (reached == target)
            {
                return [.. changes.Where(c => _values[c.Key](server) != c.Value).Select(c => (c.Key, c.Value))];
            }

            if (reached?.When?.FirstOrDefault(c => !changes.ContainsKey(c.Key) && Catalog.Default.Option(c.Key)?.Switch == true) is not { Key: not null } blocking)
            {
                return null;
            }

            changes[blocking.Key] = blocking.Value == "true" ? "false" : "true";
        }

        return null;
    }

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

        // Trickplay images, which earlier runs measured, reach the backend only with trickplay's own Enable hardware decoding on, so they don't pick it.
        var hardware = measured.Where(r => r.Type != HwType.none && r.Kind != SpeedOutputKind.Images).ToList();
        if (hardware.Count > 0)
        {
            var winners = hardware.GroupBy(r => r.Test, StringComparer.Ordinal).Select(g => g.Aggregate(Better)).ToList();
            var best = winners.GroupBy(r => (r.Type, r.Device)).OrderByDescending(g => g.Count()).First();

            // On software, any working hardware backend is suggested, however it compares: it draws less power for the same work.
            // On hardware, only outputs where the configured backend was measured too, and lost by more than noise; where they measured alike, QSV over VAAPI and a backend that is more efficient.
            SpeedResult? Mine(SpeedResult w) => measured.FirstOrDefault(r => r.Test == w.Test && Configured(r));
            bool Alike(SpeedResult w, SpeedResult mine) => Gain(w, mine) >= -Noise && Gain(w, mine) <= Noise && (w.Streams ?? -1) >= (mine.Streams ?? -1);
            bool Beats(SpeedResult w, SpeedResult mine) => Gain(w, mine) > Noise || (Alike(w, mine) && (BackendPreference.IsPreferredOver(w, mine) || ResourceComparison.Savings(w, mine).Count > 0));
            List<SpeedResult> beaten = type == HwType.none ? [.. best] : [.. best.Where(w => Mine(w) is { } mine && Beats(w, mine))];
            if ((best.Key.Type != type || (!string.IsNullOrEmpty(device) && best.Key.Device != device)) && beaten.Count > 0)
            {
                var alike = type != HwType.none && beaten.All(w => Mine(w) is { } own && Gain(w, own) <= Noise);
                var mine = beaten.Select(Mine).OfType<SpeedResult>().ToList();
                suggestions.Add(new SpeedSuggestion(SpeedSuggestionKind.FastestBackend, [.. beaten.Select(r => Label(r, shown.Settings))])
                {
                    Type = best.Key.Type,
                    Device = best.Key.Device,
                    Speed = beaten.Min(Speed),
                    Streams = Fewest(beaten),
                    StreamsCapped = FewestCapped(beaten),
                    Compared = mine.Count == beaten.Count ? [new SpeedComparedValue(type.ToString(), mine.Min(Speed), Fewest(mine), FewestCapped(mine))] : [],
                    TestVideosOnly = beaten.All(IsGenerated),
                    Preferred = alike && beaten.All(w => Mine(w) is { } own && BackendPreference.IsPreferredOver(w, own)),
                    Savings = alike ? Common(beaten.Select(w => Mine(w) is { } own ? ResourceComparison.Savings(w, own) : [])) : [],
                });
            }
        }

        // No limit is suggested: a limit makes out of network devices transcode any video whose bitrate exceeds it instead of direct playing it (MediaInfoHelper.GetMaxBitrate, StreamBuilder.IsBitrateLimitExceeded, v12.2).
        if (BitrateLimit([.. measured.Where(Configured)], shown.Settings) is { } limit)
        {
            suggestions.Add(limit.NoLimit with { Type = type, Device = device });
            suggestions.Add(limit.Limit with { Type = type, Device = device });
        }

        // Outputs that two or more backends measured and none kept real time on; one backend alone, as suites measure, doesn't show the others would fall behind.
        // Trickplay images in earlier runs are made ahead of playback, so they have no real time to keep.
        bool KeepsUp(SpeedResult r) => Speed(r) >= 1 && r.Streams != 0;
        var playback = measured.Where(r => r.Kind != SpeedOutputKind.Images).ToList();
        var tooSlow = playback.GroupBy(r => r.Test, StringComparer.Ordinal).Where(g => !g.Any(KeepsUp) && g.Select(r => (r.Type, r.Device)).Distinct().Count() > 1).ToList();

        // A test video that falls behind means real video will too: test videos encode faster. Outputs every backend falls behind on are reported on their own instead.
        var behind = playback.Where(r => Configured(r) && !KeepsUp(r) && !tooSlow.Any(g => g.Key == r.Test)).ToList();
        if (behind.Count > 0)
        {
            suggestions.Add(new SpeedSuggestion(SpeedSuggestionKind.FallsBehind, [.. behind.Select(r => Label(r, shown.Settings))])
            {
                Type = type,
                Device = device,
                Speed = behind.Min(Speed),
                Streams = Fewest(behind),
                StreamsCapped = FewestCapped(behind),
                Speeds = [.. behind.Select(r => new OutputSpeed(Label(r, shown.Settings), r.Video, r.Output, Speed(r), r.Streams, r.Capped))],
                TestVideosOnly = behind.All(IsGenerated),
            });
        }

        foreach (var fastest in tooSlow.Select(g => g.Aggregate(Better)))
        {
            suggestions.Add(new SpeedSuggestion(SpeedSuggestionKind.TooSlowEverywhere, [Label(fastest, shown.Settings)]) { Type = fastest.Type, Device = fastest.Device, Speed = Speed(fastest), Streams = fastest.Streams, StreamsCapped = fastest.Capped, TestVideosOnly = IsGenerated(fastest) });
        }

        // Suites run on QSV in place of a configured VAAPI on the same GPU, so their comparisons count for it.
        bool Compared(SpeedResult r) => Configured(r) || BackendPreference.StandsInFor(r, type, device);
        var comparisons = Comparisons(runs, Compared);

        // Comparisons against several other values, or from several runs, that suggest the same value become one suggestion; in a setting group, only those in the same row, so each choice keeps its own.
        var merged = CompareSettings(comparisons, server, type)
            .GroupBy(s => (s.Kind, s.Setting, s.Value, s.Row))
            .Select(g => g.First() with
            {
                Others = [.. g.SelectMany(s => s.Others).Distinct(StringComparer.Ordinal)],
                Outputs = [.. g.SelectMany(s => s.Outputs).Distinct(StringComparer.Ordinal)],
                Gain = g.Min(s => s.Gain),
                Speed = g.Min(s => s.Speed),
                Speeds = Slowest(g.SelectMany(s => s.Speeds)),
                Compared = [.. g.SelectMany(s => s.Compared).GroupBy(c => (c.Value, c.Row)).Select(c => c.OrderBy(v => v.Speed).First() with { Speeds = Slowest(c.SelectMany(v => v.Speeds)) })],
                TestVideosOnly = g.All(s => s.TestVideosOnly),
            })
            .ToList();

        // Of several better-quality values that all keep up, only the best is worth suggesting; the others are steps on the way.
        merged.RemoveAll(s => s.Kind == SpeedSuggestionKind.HigherQuality && !s.Current
            && merged.Any(o => o.Kind == SpeedSuggestionKind.HigherQuality && !o.Current && o.Setting == s.Setting && IsBetterQuality(o.Setting, o.Value, s.Value, type)));

        // A value already suggested as faster or more efficient needs no second suggestion for avoiding a drawback.
        merged.RemoveAll(s => s.Kind == SpeedSuggestionKind.Compatible
            && merged.Any(o => o.Kind is SpeedSuggestionKind.FasterSetting or SpeedSuggestionKind.EfficientSetting && o.Setting == s.Setting && o.Value == s.Value));
        suggestions.AddRange(OneTablePerSetting(merged, comparisons, server, type));
        return suggestions;
    }

    /// <summary>Lists each output measured with two values of one setting, from pairs of runs that differ in that setting alone, on the configured backend's results.</summary>
    /// <param name="runs">Every saved run.</param>
    /// <param name="configured">Whether a result is the configured backend's.</param>
    /// <returns>The comparisons, each pair of runs both ways round.</returns>
    private static List<SettingComparison> Comparisons(IReadOnlyList<SpeedReport> runs, Func<SpeedResult, bool> configured)
    {
        List<SettingComparison> seen = [];
        List<(SpeedReport Run, SpeedSettings Settings)> withSettings = [];
        foreach (var run in runs)
        {
            if (run.Settings is { } settings)
            {
                withSettings.Add((run, settings));
            }
        }

        var values = withSettings.Select(r => _values.ToDictionary(v => v.Key, v => v.Value(r.Settings), StringComparer.Ordinal)).ToList();
        for (var i = 0; i < withSettings.Count; i++)
        {
            for (var j = 0; j < withSettings.Count; j++)
            {
                var (a, b) = (withSettings[i], withSettings[j]);
                if (i == j || a.Run.Method != b.Run.Method || a.Run.Repeats != b.Run.Repeats)
                {
                    continue;
                }

                // Audio and burned-in subtitles describe the client; they must match too, but aren't suggested.
                var differs = _values.Keys.Where(k => values[i][k] != values[j][k]).ToList();
                if (differs.Count == 0 || a.Settings.AudioCopy != b.Settings.AudioCopy || a.Settings.BurnIn != b.Settings.BurnIn)
                {
                    continue;
                }

                foreach (var mine in a.Run.Results.Where(r => r.Fps is > 0 && string.IsNullOrEmpty(r.Variant) && !r.LowPowerDropped && configured(r) && r.Kind != SpeedOutputKind.Images))
                {
                    // A setting for another output codec (low power, CRF) doesn't reach this output, so the Intel low power suite, which switches both codecs at once, still compares each.
                    // The key's output part names the codec, also for a library test, which SpeedCatalog.Find doesn't resolve.
                    var codec = SpeedCatalog.FindOutput(mine.Test.Split('|')[^1])?.Codec;
                    var relevant = differs.Where(k => Catalog.Default.Option(k)?.OutputCodec is not { } only || only == codec).ToList();
                    if (relevant.Count != 1)
                    {
                        continue;
                    }

                    var key = relevant[0];

                    // The same command means the setting doesn't reach this output (CRF on a hardware encoder, presets VideoToolbox maps alike).
                    // A library test keeps the same key whatever file it read, so the input must match as well.
                    if (b.Run.Results.FirstOrDefault(r => r.Test == mine.Test && r.Type == mine.Type && r.Device == mine.Device && r.Input == mine.Input && r.Video == mine.Video && r.Fps is > 0 && string.IsNullOrEmpty(r.Variant) && !r.LowPowerDropped && configured(r)) is { } theirs
                        && (mine.Command is null || mine.Command != theirs.Command))
                    {
                        seen.Add(new(key, values[i][key], values[j][key], mine.Test, Label(mine, a.Settings), Gain(mine, theirs), Speed(mine), IsGenerated(mine), ResourceComparison.Savings(mine, theirs), mine.Streams, theirs.Streams, a.Settings.Tonemap || a.Settings.VppTonemap || a.Settings.VideoToolboxTonemap, mine.Capped, theirs.Capped, Speed(theirs), GroupRow(key, a.Settings, mine.Type), GroupRow(key, b.Settings, theirs.Type), mine.Video, mine.Output));
                    }
                }
            }
        }

        return seen;
    }

    /// <summary>Draws setting suggestions from runs that differ in one setting.</summary>
    /// <param name="seen">Every comparison.</param>
    /// <param name="server">The server's settings now.</param>
    /// <param name="type">The configured backend, for the server's choice in a setting group.</param>
    /// <returns>The setting suggestions.</returns>
    private static IEnumerable<SpeedSuggestion> CompareSettings(List<SettingComparison> seen, SpeedSettings server, HwType type)
    {
        // Settings a comparison with the server's value covered, and those that produced a suggestion.
        var compared = new HashSet<string>(StringComparer.Ordinal);
        var suggested = new HashSet<string>(StringComparer.Ordinal);

        // In a setting group, each pair of rows is compared on its own: double rate with YADIF and with BWDIF are different choices.
        foreach (var group in seen.GroupBy(s => (s.Key, s.Value, s.Other, s.Row, s.OtherRow)))
        {
            // Only comparisons with the server's current value say what changing it would do; where the current value wins, that's said too.
            var (key, value, other) = (group.Key.Key, group.Key.Value, group.Key.Other);
            var serverValue = _values[key](server);

            // In a setting group the server's value is current only on the server's row: double rate off with BWDIF isn't the server's choice when it runs YADIF.
            var current = value == serverValue && (group.Key.Row is null || group.Key.Row == GroupRow(key, server, type));
            if (!current && other != serverValue)
            {
                continue;
            }

            compared.Add(key);
            var streams = group.Where(s => s.Streams is not null && s.OtherStreams is not null).ToList();
            var fewest = streams.Count > 0 ? streams.Min(s => s.Streams) : null;
            var fewestOther = streams.Count > 0 ? streams.Min(s => s.OtherStreams) : null;

            // A count that hit its cap is a lower bound, which the page shows with a plus.
            var fewestCapped = streams.Count > 0 && streams.Where(s => s.Streams == fewest).All(s => s.Capped);
            var fewestOtherCapped = streams.Count > 0 && streams.Where(s => s.OtherStreams == fewestOther).All(s => s.OtherCapped);

            var gains = group.Select(s => s.Gain).Order().ToList();
            var outputs = group.Select(s => s.Label).Distinct(StringComparer.Ordinal).ToList();
            var (setGroup, row, otherRow) = (GroupOf(key)?.Key, group.First().Row, group.First().OtherRow);
            var generated = group.All(s => s.Generated);
            var (speeds, otherSpeeds) = (Slowest(group.Select(s => s.Measured)), Slowest(group.Select(s => s.OtherMeasured)));

            // A better-quality value measuring faster contradicts the catalog's order: the encoder treats the two alike, or the runs weren't alike. Neither is a reason to switch.
            var contradicted = IsBetterQuality(key, value, other, type) && gains.Any(g => g > Noise);

            // Turning tone mapping off altogether sends HDR colours to SDR players unconverted, which neither speed nor savings are worth; switching from one method to another is fine.
            var tonemapOff = key is "Tonemap" or "VppTonemap" or "VideoToolboxTonemap" && value == "false" && group.Any(s => !s.ToneMaps);

            // A value with a known drawback comes with the value that avoids it, so the choice is shown both ways.
            var avoidable = Catalog.Default.Option(key)?.CompatibleValue == other;
            if (gains.All(g => g > Noise))
            {
                if (tonemapOff || contradicted)
                {
                    continue;
                }

                suggested.Add(key);
                var faster = new SpeedSuggestion(SpeedSuggestionKind.FasterSetting, outputs) { Setting = key, Value = value, Others = [other], Gain = gains[0], Speed = group.Min(s => s.Speed), TestVideosOnly = generated, LowerQuality = IsBetterQuality(key, other, value, type), Current = current, Streams = fewest, OtherStreams = fewestOther, StreamsCapped = fewestCapped, OtherStreamsCapped = fewestOtherCapped, Group = setGroup, Row = row, Speeds = speeds, Compared = [new SpeedComparedValue(other, group.Min(s => s.OtherSpeed), fewestOther, fewestOtherCapped) { Row = otherRow, Speeds = otherSpeeds }] };
                yield return faster;
                if (avoidable)
                {
                    yield return Compatible(faster, value, group.Min(s => s.OtherSpeed), serverValue, type);
                }

                continue;
            }

            // As fast, but using less on every output: worth it on a busy server, where the CPU, memory, or GPU goes to other transcodes.
            var savings = Common(group.Select(s => s.Savings));
            if (!tonemapOff && gains.All(g => g >= -Noise && g <= Noise) && savings.Count > 0)
            {
                suggested.Add(key);
                var efficient = new SpeedSuggestion(SpeedSuggestionKind.EfficientSetting, outputs) { Setting = key, Value = value, Others = [other], Gain = gains[0], Speed = group.Min(s => s.Speed), TestVideosOnly = generated, LowerQuality = IsBetterQuality(key, other, value, type), Savings = savings, Current = current, Streams = fewest, OtherStreams = fewestOther, StreamsCapped = fewestCapped, OtherStreamsCapped = fewestOtherCapped, Group = setGroup, Row = row, Speeds = speeds, Compared = [new SpeedComparedValue(other, group.Min(s => s.OtherSpeed), fewestOther, fewestOtherCapped) { Row = otherRow, Speeds = otherSpeeds }] };
                yield return efficient;
                if (avoidable)
                {
                    yield return Compatible(efficient, value, group.Min(s => s.OtherSpeed), serverValue, type);
                }

                continue;
            }

            // Headroom is judged on real video only: a test video's speed overstates it.
            // Better quality isn't worth losing many concurrent streams, so it's suggested only while most are kept.
            var real = group.Where(s => !s.Generated).ToList();

            // Where the stream count hit its cap (or a driver's session limit), it can't show the loss, so speed stands in for it.
            var keepsStreams = real.All(s => s.Streams is { } mine && s.OtherStreams is { } theirs && !s.Capped && !s.OtherCapped && theirs > 0
                ? mine >= theirs * (1 - MaxStreamLoss)
                : s.Speed >= s.OtherSpeed * (1 - MaxStreamLoss));
            if (!contradicted && IsBetterQuality(key, value, other, type) && real.Count > 0 && real.Min(s => s.Speed) >= Headroom && keepsStreams)
            {
                suggested.Add(key);
                var realStreams = real.Where(s => s.Streams is not null && s.OtherStreams is not null).ToList();
                var quality = new SpeedSuggestion(SpeedSuggestionKind.HigherQuality, [.. real.Select(s => s.Label).Distinct(StringComparer.Ordinal)])
                {
                    Setting = key,
                    Value = value,
                    Others = [other],
                    Gain = gains[0],
                    Speed = real.Min(s => s.Speed),
                    Current = current,
                    Streams = realStreams.Count > 0 ? realStreams.Min(s => s.Streams) : null,
                    OtherStreams = realStreams.Count > 0 ? realStreams.Min(s => s.OtherStreams) : null,
                    StreamsCapped = realStreams.Count > 0 && realStreams.Where(s => s.Streams == realStreams.Min(r => r.Streams)).All(s => s.Capped),
                    OtherStreamsCapped = realStreams.Count > 0 && realStreams.Where(s => s.OtherStreams == realStreams.Min(r => r.OtherStreams)).All(s => s.OtherCapped),
                };
                quality = quality with { Group = setGroup, Row = real[0].Row, Speeds = speeds, Compared = [new SpeedComparedValue(other, real.Min(s => s.OtherSpeed), quality.OtherStreams, quality.OtherStreamsCapped) { Row = real[0].OtherRow, Speeds = otherSpeeds }] };
                yield return quality;
                if (avoidable)
                {
                    yield return Compatible(quality, value, real.Min(s => s.OtherSpeed), serverValue, type);
                }
            }
        }

        // A setting compared with the server's value, where no other value is worth suggesting, says the current one is kept, so a suite always ends in a result.
        foreach (var key in compared.Except(suggested, StringComparer.Ordinal))
        {
            var serverValue = _values[key](server);

            // In a setting group each row of the server's value is kept on its own, so one row's speeds aren't shown as another's.
            foreach (var against in seen.Where(s => s.Key == key && s.Other == serverValue).GroupBy(s => s.OtherRow).Select(g => g.ToList()))
            {
                var counted = against.Where(s => s.OtherStreams is not null).ToList();
                yield return new SpeedSuggestion(SpeedSuggestionKind.NoChange, [.. against.Select(s => s.Label).Distinct(StringComparer.Ordinal)])
                {
                    Setting = key,
                    Value = serverValue,
                    Others = [.. against.Select(s => s.Value).Distinct(StringComparer.Ordinal)],
                    Current = against[0].OtherRow is null || against[0].OtherRow == GroupRow(key, server, type),
                    Speed = against.Min(s => s.OtherSpeed),
                    Streams = counted.Count > 0 ? counted.Min(s => s.OtherStreams) : null,
                    StreamsCapped = counted.Count > 0 && counted.Where(s => s.OtherStreams == counted.Min(c => c.OtherStreams)).All(s => s.OtherCapped),
                    Group = GroupOf(key)?.Key,
                    Row = against[0].OtherRow,
                    Speeds = Slowest(against.Select(s => s.OtherMeasured)),
                    Compared = [.. against.GroupBy(s => (s.Value, s.Row)).Select(g =>
                {
                    var streams = g.Where(s => s.Streams is not null).ToList();
                    var fewest = streams.Count > 0 ? streams.Min(s => s.Streams) : null;
                    return new SpeedComparedValue(g.Key.Value, g.Min(s => s.Speed), fewest, streams.Count > 0 && streams.Where(s => s.Streams == fewest).All(s => s.Capped)) { Row = g.Key.Row, Speeds = Slowest(g.Select(s => s.Measured)) };
                })],
                    TestVideosOnly = against.All(s => s.Generated),
                };
            }
        }
    }

    /// <summary>Returns the suggestion for the value a suggested one was compared with, which avoids the suggested value's known drawback.</summary>
    /// <param name="suggested">The suggestion for the value with the drawback.</param>
    /// <param name="value">The value with the drawback.</param>
    /// <param name="speed">The slowest measured speed with the value that avoids it, as a multiple of real time.</param>
    /// <param name="serverValue">The server's current value.</param>
    /// <param name="type">The configured backend.</param>
    /// <returns>The suggestion, on the same outputs with the speeds and streams swapped.</returns>
    private static SpeedSuggestion Compatible(SpeedSuggestion suggested, string value, double speed, string serverValue, HwType type) =>
        new(SpeedSuggestionKind.Compatible, suggested.Outputs)
        {
            Setting = suggested.Setting,
            Value = suggested.Others[0],
            Others = [value],
            Gain = suggested.Gain is { } gain ? (1 / (1 + gain)) - 1 : null,
            LowerQuality = IsBetterQuality(suggested.Setting, value, suggested.Others[0], type),
            Speed = speed,
            Current = suggested.Others[0] == serverValue,
            Streams = suggested.OtherStreams,
            OtherStreams = suggested.Streams,
            StreamsCapped = suggested.OtherStreamsCapped,
            OtherStreamsCapped = suggested.StreamsCapped,
            Group = suggested.Group,
            Row = suggested.Compared.Count > 0 ? suggested.Compared[0].Row : null,
            Speeds = suggested.Compared.Count > 0 ? suggested.Compared[0].Speeds : [],
            Compared = [new SpeedComparedValue(value, suggested.Speed ?? 0, suggested.Streams, suggested.StreamsCapped) { Row = suggested.Row, Speeds = suggested.Speeds, Current = suggested.Current }],
            TestVideosOnly = suggested.TestVideosOnly,
        };

    /// <summary>Makes each setting outside a group one suggestion listing every value compared with the server's beside the recommended one, so the page shows one table per setting.</summary>
    /// <param name="merged">The setting suggestions.</param>
    /// <param name="comparisons">Every comparison.</param>
    /// <param name="server">The server's settings now.</param>
    /// <param name="type">The configured backend, for the quality order.</param>
    /// <returns>One suggestion per setting outside a group, followed by any for the value that avoids its drawback; those in a group unchanged.</returns>
    private static IEnumerable<SpeedSuggestion> OneTablePerSetting(List<SpeedSuggestion> merged, List<SettingComparison> comparisons, SpeedSettings server, HwType type)
    {
        foreach (var setting in merged.GroupBy(s => s.Setting, StringComparer.Ordinal))
        {
            if (setting.Key is not { } key)
            {
                foreach (var s in setting)
                {
                    yield return s;
                }

                continue;
            }

            var serverValue = _values[key](server);
            var against = comparisons.Where(c => c.Key == key && c.Other == serverValue).ToList();
            if (GroupOf(key) is not null || against.Count == 0)
            {
                foreach (var s in setting)
                {
                    yield return s;
                }

                continue;
            }

            List<(string Value, IReadOnlyList<OutputSpeed> Speeds)> values = [(serverValue, Slowest(against.Select(c => c.OtherMeasured))), .. against.GroupBy(c => c.Value, StringComparer.Ordinal).Select(g => (g.Key, Slowest(g.Select(c => c.Measured))))];
            var chosen = Recommended(key, [.. setting.Where(s => s.Kind != SpeedSuggestionKind.Compatible)], values, against, type);
            var speeds = values.First(v => v.Value == chosen.Value).Speeds;

            // The row's speed and streams are the table's; a better-quality value's speed, which its reason gives, from real video alone, as it was judged.
            var real = against.Where(c => !c.Generated && (chosen.Value == serverValue || c.Value == chosen.Value)).Select(c => chosen.Value == serverValue ? c.OtherSpeed : c.Speed).ToList();
            var table = chosen with
            {
                Speed = chosen.Kind == SpeedSuggestionKind.HigherQuality && real.Count > 0 ? real.Min() : speeds.Min(o => o.Speed),
                Streams = Fewest(speeds),
                StreamsCapped = FewestCapped(speeds),
                Speeds = speeds,
                Compared = [.. values.Where(v => v.Value != chosen.Value).Select(v => new SpeedComparedValue(v.Value, v.Speeds.Min(o => o.Speed), Fewest(v.Speeds), FewestCapped(v.Speeds))
                {
                    Speeds = v.Speeds,
                    Current = v.Value == serverValue,
                    LowerQuality = IsBetterQuality(key, chosen.Value, v.Value, type) ? true : IsBetterQuality(key, v.Value, chosen.Value, type) ? false : null,
                })],
            };
            yield return table;
            foreach (var s in setting.Where(s => s.Kind == SpeedSuggestionKind.Compatible))
            {
                yield return s;
            }
        }
    }

    /// <summary>Picks the suggestion a setting's table recommends: the catalog's recommended value when it was measured; otherwise the best-quality value that keeps up, then a change that measured faster or more efficient, then the server's own value.</summary>
    /// <param name="key">The option key.</param>
    /// <param name="measured">The setting's suggestions from the measurements.</param>
    /// <param name="values">Every value compared with the server's, the server's first, each with its slowest speed per output.</param>
    /// <param name="against">The comparisons with the server's value.</param>
    /// <param name="type">The configured backend, for the quality order.</param>
    /// <returns>The suggestion.</returns>
    private static SpeedSuggestion Recommended(string key, List<SpeedSuggestion> measured, List<(string Value, IReadOnlyList<OutputSpeed> Speeds)> values, List<SettingComparison> against, HwType type)
    {
        SpeedSuggestion Listing(SpeedSuggestionKind kind, string value)
        {
            var speeds = values.First(v => v.Value == value).Speeds;
            return new(kind, [.. against.Select(c => c.Label).Distinct(StringComparer.Ordinal)])
            {
                Setting = key,
                Value = value,
                Others = [.. values.Select(v => v.Value).Where(v => v != value)],
                Current = value == values[0].Value,
                Speed = speeds.Min(o => o.Speed),
                Streams = Fewest(speeds),
                StreamsCapped = FewestCapped(speeds),
                TestVideosOnly = against.All(c => c.Generated),
            };
        }

        if (Catalog.Default.Option(key)?.Recommended is { } recommended && values.Any(v => v.Value == recommended))
        {
            return Listing(SpeedSuggestionKind.RecommendedValue, recommended);
        }

        // A value as fast as the server's within noise isn't worth switching to when the server's measured faster than it by more.
        bool Outpaced(SpeedSuggestion s) => s.Kind == SpeedSuggestionKind.EfficientSetting && s.Value is { } value && measured.Any(o => o.Kind == SpeedSuggestionKind.FasterSetting && o.Current && o.Others.Contains(value, StringComparer.Ordinal));
        var quality = measured.Where(s => s.Kind == SpeedSuggestionKind.HigherQuality).ToList();
        return quality.FirstOrDefault(s => !quality.Any(o => IsBetterQuality(key, o.Value, s.Value, type)))
            ?? measured.Where(s => s.Kind is SpeedSuggestionKind.FasterSetting or SpeedSuggestionKind.EfficientSetting && !Outpaced(s))
                .OrderBy(s => s.Current)
                .ThenBy(s => s.Kind == SpeedSuggestionKind.FasterSetting ? 0 : 1)
                .ThenByDescending(s => s.Gain)
                .FirstOrDefault()
            ?? Listing(SpeedSuggestionKind.NoChange, values[0].Value);
    }

    /// <summary>Returns each output's slowest speed and fewest concurrent streams, in the order the outputs were first measured.</summary>
    /// <param name="speeds">Speeds from one or more runs.</param>
    /// <returns>One per output.</returns>
    private static IReadOnlyList<OutputSpeed> Slowest(IEnumerable<OutputSpeed> speeds) =>
        [.. speeds.GroupBy(s => s.Label, StringComparer.Ordinal).Select(g => g.First() with { Speed = g.Min(s => s.Speed), Streams = Fewest(g.ToList()), StreamsCapped = FewestCapped(g.ToList()) })];

    /// <summary>Finds the highest quality the configured backend keeps at real time when higher ones of the same codec fall behind, as the Internet streaming bitrate limit beside no limit.</summary>
    /// <param name="configured">The configured backend's measured results.</param>
    /// <param name="settings">The run's settings, for the outputs' labels.</param>
    /// <returns>No limit, suggested, and the limit beside it; null when every quality keeps up or fewer than two were measured.</returns>
    /// <remarks>H.264 decides, as the codec Jellyfin encodes to unless HEVC encoding is allowed (EncodingOptions.AllowHevcEncoding, off by default, v12.2).</remarks>
    private static (SpeedSuggestion NoLimit, SpeedSuggestion Limit)? BitrateLimit(List<SpeedResult> configured, SpeedSettings? settings)
    {
        List<(SpeedResult Result, SpeedTest Output)> found = [];
        foreach (var result in configured)
        {
            if (SpeedCatalog.Find(result.Test) is { DecodeOnly: false, OutputCodec: "h264" } output)
            {
                found.Add((result, output));
            }
        }

        var ladder = found.OrderByDescending(x => x.Output.Bitrate).ToList();
        if (ladder.Select(x => x.Output.Bitrate).Distinct().Count() < 2)
        {
            return null;
        }

        static bool KeepsUp(SpeedResult r) => Speed(r) >= 1 && r.Streams is not 0;
        var behind = ladder.Where(x => !KeepsUp(x.Result)).ToList();
        var keeping = ladder.Where(x => KeepsUp(x.Result) && behind.All(b => x.Output.Bitrate < b.Output.Bitrate)).ToList();
        if (behind.Count == 0 || keeping.Count == 0)
        {
            return null;
        }

        var best = keeping[0];
        var value = best.Output.Bitrate.ToString(CultureInfo.InvariantCulture);
        var outputs = behind.Select(x => Label(x.Result, settings)).Distinct(StringComparer.Ordinal).ToList();
        var slowest = behind.MinBy(x => Speed(x.Result)).Result;
        var none = new SpeedSuggestion(SpeedSuggestionKind.BitrateLimit, outputs)
        {
            Setting = BitrateLimitKey,
            Value = "0",
            Others = [value],
            Speed = Speed(slowest),
            Streams = slowest.Streams,
            StreamsCapped = slowest.Capped,
            OtherStreams = best.Result.Streams,
            OtherStreamsCapped = best.Result.Capped,
            Compared = [new SpeedComparedValue(value, Speed(best.Result), best.Result.Streams, best.Result.Capped)],
            TestVideosOnly = behind.All(x => IsGenerated(x.Result)),
        };
        return (none, new SpeedSuggestion(SpeedSuggestionKind.Compatible, outputs)
        {
            Setting = BitrateLimitKey,
            Value = value,
            Others = ["0"],
            Speed = Speed(best.Result),
            Streams = best.Result.Streams,
            StreamsCapped = best.Result.Capped,
            OtherStreams = slowest.Streams,
            OtherStreamsCapped = slowest.Capped,
            Compared = [new SpeedComparedValue("0", Speed(slowest), slowest.Streams, slowest.Capped)],
            LowerQuality = true,
            TestVideosOnly = none.TestVideosOnly,
        });
    }

    /// <summary>Reports whether a value gives a better picture than another.</summary>
    /// <param name="key">The setting.</param>
    /// <param name="value">The value.</param>
    /// <param name="other">The other value.</param>
    /// <param name="type">The configured backend.</param>
    /// <returns>True when the setting trades speed for quality and the value is the better-quality one; false when any is missing.</returns>
    private static bool IsBetterQuality(string? key, string? value, string? other, HwType type)
    {
        // Auto is veryfast for libx264, libx265, and QSV, and the same setting as veryfast on NVENC, AMF, and VideoToolbox (SVT-AV1 takes faster's);
        // VAAPI leaves it to the driver, so there it isn't ordered (EncodingHelper.GetEncoderParam, v12.2).
        static string Preset(string v) => v == "auto" ? "veryfast" : v;
        return key is not null && value is not null && other is not null
            && !(type == HwType.vaapi && (value == "auto" || other == "auto"))
            && Catalog.Default.Option(key) is { } option && option.IsBetterQuality(Preset(value), Preset(other));
    }

    /// <summary>Returns the better of two results for one output: more streams kept up, then faster; when they measure alike, QSV over VAAPI on the same GPU, then the more efficient one.</summary>
    /// <param name="a">One result.</param>
    /// <param name="b">The other.</param>
    /// <returns>The better one.</returns>
    private static SpeedResult Better(SpeedResult a, SpeedResult b)
    {
        var (streamsA, streamsB) = (a.Streams ?? -1, b.Streams ?? -1);
        if (streamsA == streamsB && Math.Abs(Gain(b, a)) <= Noise)
        {
            if (BackendPreference.IsPreferredOver(b, a))
            {
                return b;
            }

            if (BackendPreference.IsPreferredOver(a, b))
            {
                return a;
            }

            if (ResourceComparison.Savings(b, a).Count > 0)
            {
                return b;
            }

            if (ResourceComparison.Savings(a, b).Count > 0)
            {
                return a;
            }
        }

        return streamsB > streamsA || (streamsB == streamsA && b.Fps > a.Fps) ? b : a;
    }

    /// <summary>Returns the savings every comparison shares, each at its smallest.</summary>
    /// <param name="comparisons">Each comparison's savings.</param>
    /// <returns>The resources saved in all of them, largest first; empty when there are none.</returns>
    private static IReadOnlyList<ResourceSaving> Common(IEnumerable<IReadOnlyList<ResourceSaving>> comparisons)
    {
        var all = comparisons.ToList();
        return all.Count == 0 ? [] : [.. all[0]
            .Select(s => s.Resource)
            .Where(resource => all.All(c => c.Any(s => s.Resource == resource)))
            .Select(resource => new ResourceSaving(resource, all.Min(c => c.First(s => s.Resource == resource).Fraction)))
            .OrderByDescending(s => s.Fraction)];
    }

    /// <summary>Reports whether a result is from a generated test clip rather than a film sample, library file, or downloaded recording.</summary>
    /// <param name="r">The result.</param>
    /// <returns>True for a generated clip.</returns>
    private static bool IsGenerated(SpeedResult r) =>
        r.Kind is SpeedOutputKind.Audio or SpeedOutputKind.AudioDecode
            ? SpeedCatalog.FindAudio(r.Test.Split('|')[0]) is { Fixture.DownloadUrl: null }
            : SpeedCatalog.FindVideo(r.Test.Split('|')[0]) is { Title: null, File: null };

    /// <summary>Returns how much faster one result is than another, as a fraction.</summary>
    /// <param name="a">The result.</param>
    /// <param name="b">The one it's compared with.</param>
    /// <returns>For example 0.2 for 20% faster.</returns>
    private static double Gain(SpeedResult a, SpeedResult b) => (Speed(a) / Speed(b)) - 1;

    /// <summary>Returns a result's speed as a multiple of real time.</summary>
    /// <param name="r">The result.</param>
    /// <returns>The speed; fps alone when the frame rate wasn't recorded, and 0 without fps.</returns>
    private static double Speed(SpeedResult r) => (r.Fps ?? 0) / (r.FrameRate is > 0 and var rate ? rate : 1);

    /// <summary>Returns what was tested: the input, and the output with its audio.</summary>
    /// <param name="r">The result.</param>
    /// <param name="settings">The run's settings, for the audio, or null when not recorded.</param>
    /// <returns>For example <c>Live-action + CGI (1080p VP9, 24 fps, stereo Opus) → H.264, 8 Mbps, stereo AAC</c>.</returns>
    private static string Label(SpeedResult r, SpeedSettings? settings)
    {
        if (r.Video is null || r.Output is null)
        {
            return r.Label ?? r.Test;
        }

        // A decode test has no output audio; the others transcode it or copy it, as the run's Audio option says.
        var decodeOnly = SpeedCatalog.FindOutput(r.Test.Split('|')[^1]) is { Codec: null };
        var audio = settings is null || decodeOnly ? string.Empty : settings.AudioCopy ? ", audio copied" : ", stereo AAC";
        return r.Video + (string.IsNullOrEmpty(r.Input) ? string.Empty : " (" + r.Input + ")") + " \u2192 " + r.Output + audio;
    }

    /// <summary>Returns the catalog setting group a setting is in.</summary>
    /// <param name="key">The option key.</param>
    /// <returns>The group, or null when it's in none.</returns>
    private static CatalogSettingGroup? GroupOf(string key) => Catalog.Default.SettingGroups.FirstOrDefault(g => g.Settings.Contains(key));

    /// <summary>Returns the choice a run's settings make in a setting's group, on a backend.</summary>
    /// <param name="key">The option key.</param>
    /// <param name="settings">The run's settings.</param>
    /// <param name="type">The backend it ran on.</param>
    /// <returns>The first row whose backends and conditions the run meets, or null when the setting is in no group.</returns>
    private static string? GroupRow(string key, SpeedSettings settings, HwType type) =>
        GroupOf(key) is { } group ? RowOf(group, settings, type)?.Label : null;

    /// <summary>Returns the choice some settings make in a group, on a backend.</summary>
    /// <param name="group">The group.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="type">The backend.</param>
    /// <returns>The first row whose backends and conditions they meet, or null.</returns>
    private static CatalogGroupRow? RowOf(CatalogSettingGroup group, SpeedSettings settings, HwType type) =>
        group.Rows.FirstOrDefault(r => (r.Backends is null || r.Backends.Contains(type)) && (r.When ?? new Dictionary<string, string>()).All(c => _values[c.Key](settings) == c.Value));

    /// <summary>Returns the fewest concurrent streams any result kept.</summary>
    /// <param name="results">The results.</param>
    /// <returns>The count, or null when none was counted.</returns>
    private static int? Fewest(IReadOnlyCollection<SpeedResult> results) =>
        results.Where(r => r.Streams is not null).Select(r => r.Streams).DefaultIfEmpty(null).Min();

    /// <summary>Reports whether the fewest concurrent streams any result kept hit the count's cap, so it's a lower bound.</summary>
    /// <param name="results">The results.</param>
    /// <returns>True when every result at that count was capped.</returns>
    private static bool FewestCapped(IReadOnlyCollection<SpeedResult> results) =>
        Fewest(results) is { } fewest && results.Where(r => r.Streams == fewest).All(r => r.Capped);

    /// <summary>Returns the fewest concurrent streams kept on any output.</summary>
    /// <param name="speeds">The speeds per output.</param>
    /// <returns>The count, or null when none was counted.</returns>
    private static int? Fewest(IReadOnlyCollection<OutputSpeed> speeds) =>
        speeds.Where(s => s.Streams is not null).Select(s => s.Streams).DefaultIfEmpty(null).Min();

    /// <summary>Reports whether the fewest concurrent streams kept on any output hit the count's cap, so it's a lower bound.</summary>
    /// <param name="speeds">The speeds per output.</param>
    /// <returns>True when every output at that count was capped.</returns>
    private static bool FewestCapped(IReadOnlyCollection<OutputSpeed> speeds) =>
        Fewest(speeds) is { } fewest && speeds.Where(s => s.Streams == fewest).All(s => s.StreamsCapped);

    /// <summary>Formats a switch as the catalog keys it.</summary>
    /// <param name="on">The switch.</param>
    /// <returns><c>true</c> or <c>false</c>.</returns>
    private static string Flag(bool on) => on ? "true" : "false";

    /// <summary>Formats a number as the catalog keys it.</summary>
    /// <param name="value">The number.</param>
    /// <returns>For example <c>0.5</c>.</returns>
    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
}
