using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Source-generated log messages.</summary>
internal static partial class Log
{
    /// <summary>Logs a refused probe.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Transcoding Diagnostics skipped: a session is transcoding, so results would be unreliable.")]
    public static partial void SkippedBusy(ILogger logger);

    /// <summary>Logs a completed probe.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="viable">Number of viable backends.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Transcoding Diagnostics finished: {Viable} viable backend(s).")]
    public static partial void Completed(ILogger logger, int viable);

    /// <summary>Logs a failed probe.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Transcoding Diagnostics failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    /// <summary>Logs a completed speed run.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="results">Number of measurements.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Transcoding Diagnostics performance test finished: {Results} measurement(s).")]
    public static partial void SpeedCompleted(ILogger logger, int results);

    /// <summary>Logs a cancelled speed run.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="results">Measurements finished before it was cancelled.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Transcoding Diagnostics performance test cancelled after {Results} measurement(s).")]
    public static partial void SpeedCancelled(ILogger logger, int results);

    /// <summary>Logs a diagnostics zip that couldn't be saved; the report is still saved.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Transcoding Diagnostics couldn't save its diagnostics zip.")]
    public static partial void DiagnosticsFailed(ILogger logger, Exception exception);

    /// <summary>Logs one changed encoding setting.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="user">The admin who made the change.</param>
    /// <param name="setting">The setting key.</param>
    /// <param name="oldValue">The value before.</param>
    /// <param name="newValue">The value after.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Transcoding Diagnostics changed {Setting} from {OldValue} to {NewValue} for {User}.")]
    public static partial void SettingChanged(ILogger logger, string user, string setting, string oldValue, string newValue);
}
