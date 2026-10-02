using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Probing;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>Writes the latest report's advice to Jellyfin's encoding settings, keeps a history, and reverts.</summary>
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

    private readonly Func<EncodingOptions> _read;
    private readonly Action<EncodingOptions> _save;
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
    public SettingsService(IConfigurationManager config, IMediaEncoder mediaEncoder, ProbeService probes, IApplicationPaths paths, ILogger<SettingsService> logger)
        : this(
            () => Clone(config.GetEncodingOptions()),
            options => config.SaveConfiguration(EncodingKey, options),
            async ct => await probes.LatestJsonAsync(ct) is { } json ? ReportStore.Deserialize(json) : null,
            () => probes.Status.State == ProbeState.Running,
            () => mediaEncoder.EncoderPath,
            HistoryPath(paths),
            TimeProvider.System,
            logger)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SettingsService"/> class with injected behaviour.</summary>
    /// <param name="read">Returns a copy of the current encoding options.</param>
    /// <param name="save">Saves encoding options.</param>
    /// <param name="report">Loads the latest report.</param>
    /// <param name="probing">Reports whether a probe is running.</param>
    /// <param name="encoderPath">Returns the server's ffmpeg path.</param>
    /// <param name="historyPath">Where the history is kept.</param>
    /// <param name="time">Clock for history entries.</param>
    /// <param name="logger">Logger.</param>
    internal SettingsService(Func<EncodingOptions> read, Action<EncodingOptions> save, Func<CancellationToken, Task<CapabilityReport?>> report, Func<bool> probing, Func<string> encoderPath, string historyPath, TimeProvider time, ILogger logger)
    {
        _read = read;
        _save = save;
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
                var (report, refusal) = await CurrentReportAsync(cancellationToken);
                if (report is null)
                {
                    return refusal!;
                }

                var options = _read();
                var backend = ConfiguredBackend(report, options);
                if (backend is null)
                {
                    return Refuse($"The configured backend ({options.HardwareAccelerationType}) didn't work in the last probe.");
                }

                foreach (var change in changes)
                {
                    var advice = backend.Settings.FirstOrDefault(a => a.Setting == change.Setting);
                    if (advice is null || advice.State == SettingState.NotTested || change.Value != (advice.State == SettingState.TurnOn))
                    {
                        return Refuse($"{change.Setting} = {EncodingSettings.Format(change.Value)} doesn't match the last probe.");
                    }
                }

                var values = changes.Select(c => (c.Setting, EncodingSettings.Format(c.Value))).ToList();
                return await WriteAsync(options, values, HistoryKind.Apply, user, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Switches the hardware acceleration backend and device to one the latest report found working.</summary>
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
                var (report, refusal) = await CurrentReportAsync(cancellationToken);
                if (report is null)
                {
                    return refusal!;
                }

                if (!Enum.TryParse<HwType>(choice.Type, out var type)
                    || !report.Backends.Any(b => b.Type == type && b.Device == choice.Device && b.Verdict == BackendVerdict.Viable))
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
                var current = entry.Changes.ToLookup(c => EncodingSettings.Read(options, c.Setting) == c.NewValue);
                var values = current[true].Select(c => (c.Setting, c.OldValue)).ToList();
                var skipped = current[false].Select(c => c.Setting).ToList();

                history[index] = entry with { RevertedUtc = _time.GetUtcNow() };
                var result = await WriteAsync(options, values, HistoryKind.Revert, user, cancellationToken, history);
                return skipped.Count == 0 ? result : result with { Reason = "Changed since, left as is: " + string.Join(", ", skipped) };
            },
            cancellationToken);

    /// <summary>Returns the history, oldest first.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries.</returns>
    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(CancellationToken cancellationToken) => await ReadHistoryAsync(cancellationToken);

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

    /// <summary>Returns where the history is kept.</summary>
    /// <param name="paths">Server paths.</param>
    /// <returns>The file path.</returns>
    private static string HistoryPath(IApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.DataPath, "hwprobe", "history.json");
    }

    /// <summary>Copies encoding options so a failed save leaves the server's copy untouched.</summary>
    /// <param name="options">The options.</param>
    /// <returns>A copy.</returns>
    private static EncodingOptions Clone(EncodingOptions options) =>
        JsonSerializer.Deserialize<EncodingOptions>(JsonSerializer.Serialize(options, _json), _json)!;

    /// <summary>Finds the report row for the backend and device the server is configured to use.</summary>
    /// <param name="report">The latest report.</param>
    /// <param name="options">The current encoding options.</param>
    /// <returns>The viable row, or null.</returns>
    private static BackendReport? ConfiguredBackend(CapabilityReport report, EncodingOptions options)
    {
        var type = (HwType)(int)options.HardwareAccelerationType;
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

    /// <summary>Loads the latest report, if it was made with the server's current ffmpeg.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The report, or null and the refusal.</returns>
    private async Task<(CapabilityReport? Report, ApplyResult? Refusal)> CurrentReportAsync(CancellationToken cancellationToken)
    {
        var report = await _report(cancellationToken);
        if (report is null)
        {
            return (null, new ApplyResult(ApplyOutcome.NoReport, [], "Run a probe first."));
        }

        return report.Ffmpeg.Path == _encoderPath()
            ? (report, null)
            : (null, Refuse("The last probe used a different ffmpeg. Run the probe again."));
    }

    /// <summary>Writes the values that differ, saves, and records the history.</summary>
    /// <param name="options">A copy of the current options.</param>
    /// <param name="values">Setting keys and values.</param>
    /// <param name="kind">What the write is.</param>
    /// <param name="user">The admin making the change.</param>
    /// <param name="cancellationToken">Cancels the history write.</param>
    /// <param name="history">The history to append to, when the caller already changed it; null to read it.</param>
    /// <returns>The applied result.</returns>
    private async Task<ApplyResult> WriteAsync(EncodingOptions options, IReadOnlyList<(string Setting, string Value)> values, HistoryKind kind, string user, CancellationToken cancellationToken, List<HistoryEntry>? history = null)
    {
        List<AppliedChange> changed = [];
        foreach (var (setting, value) in values)
        {
            var old = EncodingSettings.Read(options, setting);
            if (old != value)
            {
                EncodingSettings.Write(options, setting, value);
                changed.Add(new AppliedChange(setting, old, value));
            }
        }

        history ??= await ReadHistoryAsync(cancellationToken);
        if (changed.Count > 0)
        {
            _save(options);
            history.Add(new HistoryEntry(_time.GetUtcNow(), user, kind, changed));
            foreach (var change in changed)
            {
                Log.SettingChanged(_logger, user, change.Setting, change.OldValue, change.NewValue);
            }
        }

        await WriteHistoryAsync(history, cancellationToken);
        var restart = changed.Any(c => _restartKeys.Contains(c.Setting));
        _restartRequired |= restart;
        return new ApplyResult(ApplyOutcome.Applied, changed, null) { RestartRequired = restart };
    }

    /// <summary>Reads the history file.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries, oldest first; empty when there's no file.</returns>
    private async Task<List<HistoryEntry>> ReadHistoryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_historyPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_historyPath);
        return await JsonSerializer.DeserializeAsync<List<HistoryEntry>>(stream, _json, cancellationToken) ?? [];
    }

    /// <summary>Writes the history file through a temporary file, so a crash can't leave it half written.</summary>
    /// <param name="history">The entries.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private async Task WriteHistoryAsync(List<HistoryEntry> history, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
        var temp = _historyPath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, history, _json, cancellationToken);
        }

        File.Move(temp, _historyPath, overwrite: true);
    }
}
