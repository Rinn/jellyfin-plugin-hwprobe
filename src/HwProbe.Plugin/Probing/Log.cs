using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Source-generated log messages.</summary>
internal static partial class Log
{
    /// <summary>Logs a refused probe.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "HwProbe skipped: a session is transcoding, so results would be unreliable.")]
    public static partial void SkippedBusy(ILogger logger);

    /// <summary>Logs a completed probe.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="viable">Number of viable backends.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "HwProbe finished: {Viable} viable backend(s).")]
    public static partial void Completed(ILogger logger, int viable);

    /// <summary>Logs a failed probe.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "HwProbe failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    /// <summary>Logs one changed encoding setting.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="user">The admin who made the change.</param>
    /// <param name="setting">The setting key.</param>
    /// <param name="oldValue">The value before.</param>
    /// <param name="newValue">The value after.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "HwProbe changed {Setting} from {OldValue} to {NewValue} for {User}.")]
    public static partial void SettingChanged(ILogger logger, string user, string setting, string oldValue, string newValue);
}
