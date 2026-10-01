namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>How an ffmpeg launch ended.</summary>
public enum FfmpegRunStatus
{
    /// <summary>The process exited on its own; see the exit code.</summary>
    Exited,

    /// <summary>The hard timeout fired and the process tree was killed.</summary>
    TimedOut,

    /// <summary>The process could not be started at all.</summary>
    LaunchFailed,
}
