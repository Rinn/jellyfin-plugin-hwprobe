namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Where the probed ffmpeg binary was found, in discovery-priority order.</summary>
public enum FfmpegSource
{
    /// <summary>The <c>--ffmpeg</c> command-line option.</summary>
    CommandLine,

    /// <summary>The <c>JELLYFIN_FFMPEG</c> environment variable.</summary>
    EnvironmentVariable,

    /// <summary>A known Jellyfin install location.</summary>
    KnownPath,

    /// <summary>The first <c>ffmpeg</c> on <c>PATH</c>; likely not the binary the server uses.</summary>
    SystemPath,
}
