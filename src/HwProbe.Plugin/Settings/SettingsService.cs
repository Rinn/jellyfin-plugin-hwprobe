using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.Core.Storage;
using Jellyfin.Plugin.HwProbe.Probing;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>Writes the latest report's advice to Jellyfin's encoding and trickplay settings, keeps a history, and reverts.</summary>
public sealed class SettingsService : IDisposable
{
    private const string EncodingKey = "encoding";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] _restartKeys =
    [
        nameof(EncodingOptions.HardwareAccelerationType),
        nameof(EncodingOptions.VaapiDevice),
        nameof(EncodingOptions.QsvDevice),
    ];

    private readonly Func<ServerSettings> _read;
    private readonly Action<EncodingOptions> _saveEncoding;
    private readonly Action<TrickplayOptions> _saveTrickplay;
    private readonly Func<CancellationToken, Task<CapabilityReport?>> _report;
    private readonly Func<bool> _probing;
    private readonly Func<string> _encoderPath;
    private readonly string _historyPath;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _restartRequired;

    /// <summary>Initializes a new instance of the <see cref="SettingsService"/> class from server services.</summary>
    /// <param name="config">The server's configuration manager.</param>
    /// <param name="mediaEncoder">The media encoder, for the ffmpeg path.</param>
    /// <param name="probes">The probe service, for the latest report and whether a probe is running.</param>
    /// <param name="paths">Server paths, for the history file.</param>
    /// <param name="logger">Logger.</param>
    public SettingsService(IServerConfigurationManager config, IMediaEncoder mediaEncoder, ProbeService probes, IApplicationPaths paths, ILogger<SettingsService> logger)
        : this(
            () => new ServerSettings(Clone(config.GetEncodingOptions()), Clone(config.Configuration.TrickplayOptions)) { Streaming = new StreamingOptions { RemoteClientBitrateLimit = config.Configuration.RemoteClientBitrateLimit } },
            options => config.SaveConfiguration(EncodingKey, options),
            options =>
            {
                config.Configuration.TrickplayOptions = options;
                config.SaveConfiguration();
            },
            async ct => await probes.LatestJsonAsync(ct) is { } json ? ReportStore.Deserialize(json) : null,
            () => probes.Status.State == ProbeState.Running,
            () => mediaEncoder.EncoderPath,
            HistoryPath(paths),
            TimeProvider.System,
            logger)
    {
        ArgumentNullException.ThrowIfNull(probes);
        Suggestions = probes.SpeedSuggestionsAsync;
        SaveStreaming = options =>
        {
            config.Configuration.RemoteClientBitrateLimit = options.RemoteClientBitrateLimit;
            config.SaveConfiguration();
        };
    }

    /// <summary>Initializes a new instance of the <see cref="SettingsService"/> class with injected behaviour.</summary>
    /// <param name="read">Returns copies of the current encoding and trickplay options.</param>
    /// <param name="saveEncoding">Saves encoding options.</param>
    /// <param name="saveTrickplay">Saves trickplay options.</param>
    /// <param name="report">Loads the latest report.</param>
    /// <param name="probing">Reports whether a probe is running.</param>
    /// <param name="encoderPath">Returns the server's ffmpeg path.</param>
    /// <param name="historyPath">Where the history is kept.</param>
    /// <param name="time">Clock for history entries.</param>
    /// <param name="logger">Logger.</param>
    internal SettingsService(Func<ServerSettings> read, Action<EncodingOptions> saveEncoding, Action<TrickplayOptions> saveTrickplay, Func<CancellationToken, Task<CapabilityReport?>> report, Func<bool> probing, Func<string> encoderPath, string historyPath, TimeProvider time, ILogger logger)
    {
        _read = read;
        _saveEncoding = saveEncoding;
        _saveTrickplay = saveTrickplay;
        _report = report;
        _probing = probing;
        _encoderPath = encoderPath;
        _historyPath = historyPath;
        _time = time;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether HwProbe changed the backend or device since Jellyfin started.</summary>
    /// <remarks>Kept in memory, so restarting Jellyfin clears it.</remarks>
    public bool RestartRequired => _restartRequired;

    /// <summary>Gets what saves the streaming options.</summary>
    internal Action<StreamingOptions> SaveStreaming { get; init; } = _ => { };

    /// <summary>Gets what draws the suggestions from performance tests, for a run or the latest.</summary>
    internal Func<string?, CancellationToken, Task<IReadOnlyList<SpeedSuggestion>>> Suggestions { get; init; } = (_, _) => Task.FromResult<IReadOnlyList<SpeedSuggestion>>([]);

    /// <summary>Applies options from the advice for the backend the server is configured to use.</summary>
    /// <param name="changes">The options and values; each must match the latest report's advice.</param>
    /// <param name="user">The admin making the change.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The result.</returns>
    public Task<ApplyResult> ApplyAsync(IReadOnlyList<SettingChange> changes, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return LockedAsync(
            async () =>
            {
                var report = await _report(cancellationToken);
                if (IsRefused(report, out var refusal))
                {
                    return refusal;
                }

                var options = _read();
                var backend = ConfiguredBackend(report, options.Encoding);
                if (backend is null)
                {
                    return Refuse($"The configured backend ({options.Encoding.HardwareAccelerationType}) didn't work in the last probe.");
                }

                foreach (var change in changes)
                {
                    var advice = backend.Settings.FirstOrDefault(a => a.Setting == change.Setting);
                    if (advice is null || advice.State == SettingState.NotTested || change.Value != (advice.State is SettingState.TurnOn or SettingState.Optional))
                    {
                        return Refuse($"{change.Setting} = {EncodingSettings.Format(change.Value)} doesn't match the last probe.");
                    }
                }

                var values = changes.Select(c => (c.Setting, EncodingSettings.Format(c.Value))).ToList();
                return await WriteAsync(options, values, HistoryKind.Apply, user, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Applies a setting value a performance test measured.</summary>
    /// <param name="change">The option and value; a current suggestion must show it.</param>
    /// <param name="user">The admin making the change.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The result.</returns>
    public Task<ApplyResult> ApplyMeasuredAsync(MeasuredChange change, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        return LockedAsync(
            async () =>
            {
                // A setting's table offers every value it lists but the server's own, even one that falls behind real time; a group's settings change together, through its rows.
                var offered = (await Suggestions(change.Run, cancellationToken)).Where(s => s.Setting == change.Setting && s.Group is null).Any(s =>
                    (s.Kind is SpeedSuggestionKind.FasterSetting or SpeedSuggestionKind.HigherQuality or SpeedSuggestionKind.Compatible or SpeedSuggestionKind.EfficientSetting or SpeedSuggestionKind.BitrateLimit or SpeedSuggestionKind.RecommendedValue
                        && !s.Current && s.Value == change.Value)
                    || s.Compared.Any(c => c.Value == change.Value && !c.Current));
                var options = _read();

                // Suggestions don't know the bitrate limit, so the server's own value is checked here too.
                if (!offered || MeasuredSettings.ToSetting(change.Setting, change.Value) is not { } setting || options.Read(setting.Setting) == setting.Value)
                {
                    return Refuse($"{change.Setting} = {change.Value} isn't a measured value the server can switch to.");
                }

                return await WriteAsync(options, [setting], HistoryKind.Apply, user, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Applies a choice in a setting group that a performance test measured, changing every setting the choice needs at once.</summary>
    /// <param name="choice">The group and row; a suggestion for the run must show the row.</param>
    /// <param name="user">The admin making the change.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The result.</returns>
    public Task<ApplyResult> ApplyMeasuredRowAsync(MeasuredRow choice, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return LockedAsync(
            async () =>
            {
                var measured = (await Suggestions(choice.Run, cancellationToken)).Any(s => s.Group == choice.Group && (s.Row == choice.Row || s.Compared.Any(c => c.Row == choice.Row)));
                var options = _read();
                var changes = measured ? SpeedAdvisor.GroupRowChanges(choice.Group, choice.Row, Probing.ProbeService.SettingsFrom(options.Encoding), Probing.ProbeService.BackendFrom(options.Encoding).Type) : null;
                List<(string Setting, string Value)> settings = [.. (changes ?? []).Select(c => MeasuredSettings.ToSetting(c.Key, c.Value)).OfType<(string Setting, string Value)>()];
                if (changes is null || settings.Count == 0 || settings.Count != changes.Count)
                {
                    return Refuse($"{choice.Row} isn't a measured choice the server can switch to.");
                }

                return await WriteAsync(options, settings, HistoryKind.Apply, user, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Switches the hardware acceleration backend and device to one the latest report found working, or to software (<c>none</c>).</summary>
    /// <param name="choice">The backend and device.</param>
    /// <param name="user">The admin making the change.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The result; Jellyfin must restart for the switch to take full effect.</returns>
    public Task<ApplyResult> UseBackendAsync(BackendChoice choice, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return LockedAsync(
            async () =>
            {
                var report = await _report(cancellationToken);
                if (IsRefused(report, out var refusal))
                {
                    return refusal;
                }

                // Software (none) always works, so it needs no probe result.
                if (!Enum.TryParse<HwType>(choice.Type, out var type)
                    || (type != HwType.none && !report.Backends.Any(b => b.Type == type && b.Device == choice.Device && b.Verdict == BackendVerdict.Viable)))
                {
                    return Refuse($"{choice.Type} on {choice.Device} didn't work in the last probe.");
                }

                List<(string, string)> values = [(nameof(EncodingOptions.HardwareAccelerationType), choice.Type)];
                if (type == HwType.vaapi)
                {
                    values.Add((nameof(EncodingOptions.VaapiDevice), choice.Device));
                }
                else if (type == HwType.qsv)
                {
                    values.Add((nameof(EncodingOptions.QsvDevice), choice.Device));
                }

                return await WriteAsync(_read(), values, HistoryKind.Backend, user, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Undoes the most recent apply or backend switch that hasn't been reverted.</summary>
    /// <param name="user">The admin making the change.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The result; settings changed again since are left alone and named in the reason.</returns>
    public Task<ApplyResult> RevertAsync(string user, CancellationToken cancellationToken) =>
        LockedAsync(
            async () =>
            {
                var history = await ReadHistoryAsync(cancellationToken);
                var index = history.FindLastIndex(e => e.Kind != HistoryKind.Revert && e.RevertedUtc is null);
                if (index < 0)
                {
                    return new ApplyResult(ApplyOutcome.NothingToRevert, [], "Nothing to revert.");
                }

                // A setting changed again after the apply is someone else's choice now.
                var options = _read();
                var entry = history[index];
                var current = entry.Changes.ToLookup(c => options.Read(c.Setting) == c.NewValue);
                var values = current[true].Select(c => (c.Setting, c.OldValue)).ToList();
                var skipped = current[false].Select(c => c.Setting).ToList();

                history[index] = entry with { RevertedUtc = _time.GetUtcNow() };
                var result = await WriteAsync(options, values, HistoryKind.Revert, user, cancellationToken, history);
                return skipped.Count == 0 ? result : result with { Reason = "Changed since, left as is: " + string.Join("; ", skipped.Select(k => SettingsAdvisor.LabelFor(k) ?? k)) };
            },
            cancellationToken);

    /// <summary>Returns the history, oldest first, with labels filled in for changes recorded without one.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries.</returns>
    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(CancellationToken cancellationToken) =>
        [.. (await ReadHistoryAsync(cancellationToken)).Select(e => e with { Changes = [.. e.Changes.Select(c => c with { Label = c.Label ?? SettingsAdvisor.LabelFor(c.Setting) ?? MeasuredSettings.LabelFor(c.Setting) })] })];

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

    /// <summary>Deletes the change history, which leaves nothing to revert.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>False while another change is being written.</returns>
    public async Task<bool> ClearHistoryAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            File.Delete(_historyPath);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns where the history is kept.</summary>
    /// <param name="paths">Server paths.</param>
    /// <returns>The file path.</returns>
    private static string HistoryPath(IApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.DataPath, "hwprobe", "history.json");
    }

    /// <summary>Copies options so a failed save leaves the server's copy untouched.</summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <param name="options">The options.</param>
    /// <returns>A copy.</returns>
    private static T Clone<T>(T options) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(options, _json), _json) ?? throw new JsonException($"{typeof(T).Name} didn't survive a JSON round trip.");

    /// <summary>Finds the report row for the backend and device the server is configured to use.</summary>
    /// <param name="report">The latest report.</param>
    /// <param name="options">The current encoding options.</param>
    /// <returns>The viable row, the software row for none, or null.</returns>
    private static BackendReport? ConfiguredBackend(CapabilityReport report, EncodingOptions options)
    {
        var type = (HwType)(int)options.HardwareAccelerationType;
        if (type == HwType.none)
        {
            return report.Software;
        }

        var device = type switch
        {
            HwType.vaapi => options.VaapiDevice,
            HwType.qsv => options.QsvDevice,
            _ => null,
        };
        var viable = report.Backends.Where(b => b.Type == type && b.Verdict == BackendVerdict.Viable).ToList();
        return string.IsNullOrEmpty(device) ? viable.FirstOrDefault() : viable.FirstOrDefault(b => b.Device == device);
    }

    /// <summary>Builds a refusal.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The result.</returns>
    private static ApplyResult Refuse(string reason) => new(ApplyOutcome.Rejected, [], reason);

    /// <summary>Runs a write while holding the gate, refusing if a probe or another write is running.</summary>
    /// <param name="write">The write.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The result.</returns>
    private async Task<ApplyResult> LockedAsync(Func<Task<ApplyResult>> write, CancellationToken cancellationToken)
    {
        if (_probing() || !await _gate.WaitAsync(0, cancellationToken))
        {
            return new ApplyResult(ApplyOutcome.Busy, [], "A probe or another change is running.");
        }

        try
        {
            return await write();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reports whether a write is refused because there's no report, or it was made with another ffmpeg than the server's.</summary>
    /// <param name="report">The latest report, or null.</param>
    /// <param name="refusal">Receives the refusal.</param>
    /// <returns>True when refused.</returns>
    private bool IsRefused([NotNullWhen(false)] CapabilityReport? report, [NotNullWhen(true)] out ApplyResult? refusal)
    {
        if (report is null)
        {
            refusal = new ApplyResult(ApplyOutcome.NoReport, [], "Run a probe first.");
            return true;
        }

        if (report.Ffmpeg.Path != _encoderPath())
        {
            refusal = Refuse("The last probe used a different ffmpeg. Run the probe again.");
            return true;
        }

        refusal = null;
        return false;
    }

    /// <summary>Writes the values that differ, saves, and records the history.</summary>
    /// <param name="options">A copy of the current options.</param>
    /// <param name="values">Setting keys and values.</param>
    /// <param name="kind">What the write is.</param>
    /// <param name="user">The admin making the change.</param>
    /// <param name="cancellationToken">Cancels the history write.</param>
    /// <param name="history">The history to append to, when the caller already changed it; null to read it.</param>
    /// <returns>The applied result.</returns>
    private async Task<ApplyResult> WriteAsync(ServerSettings options, IReadOnlyList<(string Setting, string Value)> values, HistoryKind kind, string user, CancellationToken cancellationToken, List<HistoryEntry>? history = null)
    {
        List<AppliedChange> changed = [];
        foreach (var (setting, value) in values)
        {
            var old = options.Read(setting);
            if (old != value)
            {
                options.Write(setting, value);
                changed.Add(new AppliedChange(setting, old, value) { Label = SettingsAdvisor.LabelFor(setting) ?? MeasuredSettings.LabelFor(setting) });
            }
        }

        history ??= await ReadHistoryAsync(cancellationToken);
        if (changed.Count > 0)
        {
            if (changed.Any(c => !ServerSettings.IsTrickplay(c.Setting) && !StreamingSettings.IsKnown(c.Setting)))
            {
                _saveEncoding(options.Encoding);
            }

            if (changed.Any(c => StreamingSettings.IsKnown(c.Setting)))
            {
                SaveStreaming(options.Streaming);
            }

            if (changed.Any(c => ServerSettings.IsTrickplay(c.Setting)))
            {
                _saveTrickplay(options.Trickplay);
            }

            history.Add(new HistoryEntry(_time.GetUtcNow(), user, kind, changed));
            foreach (var change in changed)
            {
                Log.SettingChanged(_logger, user, change.Setting, change.OldValue, change.NewValue);
            }
        }

        // The settings are saved by now, so the restart notice doesn't depend on the history write.
        var restart = changed.Any(c => _restartKeys.Contains(c.Setting));
        _restartRequired |= restart;
        await WriteHistoryAsync(history, cancellationToken);
        return new ApplyResult(ApplyOutcome.Applied, changed, null) { RestartRequired = restart };
    }

    /// <summary>Reads the history file.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries, oldest first; empty when there's no file.</returns>
    private async Task<List<HistoryEntry>> ReadHistoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(_historyPath);
            return await JsonSerializer.DeserializeAsync<List<HistoryEntry>>(stream, _json, cancellationToken) ?? [];
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // None yet, or cleared during the read.
            return [];
        }
    }

    /// <summary>Writes the history file through a temporary file, so a crash can't leave it half written.</summary>
    /// <param name="history">The entries.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private Task WriteHistoryAsync(List<HistoryEntry> history, CancellationToken cancellationToken) =>
        AtomicFile.WriteAsync(_historyPath, (stream, ct) => JsonSerializer.SerializeAsync(stream, history, _json, ct), cancellationToken);
}
