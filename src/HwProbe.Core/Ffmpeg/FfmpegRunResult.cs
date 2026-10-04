namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Everything observed from one ffmpeg launch.</summary>
/// <param name="Status">How the launch ended.</param>
/// <param name="ExitCode">The exit code, or null unless <see cref="FfmpegRunStatus.Exited"/>.</param>
/// <param name="Stdout">Complete standard output.</param>
/// <param name="Stderr">Complete standard error.</param>
/// <param name="Frames">The last <c>frame=</c> value from <c>-progress pipe:1</c>, or null if none.</param>
/// <param name="Duration">Wall-clock time from launch to reaping.</param>
/// <param name="LaunchError">Why the launch failed, or null unless <see cref="FfmpegRunStatus.LaunchFailed"/>.</param>
public sealed record FfmpegRunResult(
    FfmpegRunStatus Status,
    int? ExitCode,
    string Stdout,
    string Stderr,
    long? Frames,
    TimeSpan Duration,
    string? LaunchError)
{
    /// <summary>Gets when progress was first and last reported, or null when no report had frames done.</summary>
    public FrameTiming? Timing { get; init; }

    /// <summary>Gets what the run used, when the invocation asked for it and the platform reports it.</summary>
    public Resources.ResourceUsage? Resources { get; init; }
}
