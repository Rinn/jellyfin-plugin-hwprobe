namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Process exit codes; stable for CI and the M2 plugin.</summary>
internal enum HwProbeExitCode
{
    /// <summary>Probing completed, including "no hardware found".</summary>
    Success = 0,

    /// <summary>No backend is viable and <c>--expect-hw</c> was passed.</summary>
    NoViableBackend = 1,

    /// <summary>ffmpeg not found, too old, or Libav.</summary>
    FfmpegUnusable = 2,

    /// <summary>Unexpected failure inside hwprobe.</summary>
    InternalError = 3,

    /// <summary>Invalid command-line usage (sysexits EX_USAGE).</summary>
    UsageError = 64,
}
