namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Launches ffmpeg and reports what it did.</summary>
public interface IFfmpegRunner
{
    /// <summary>Runs one invocation to completion, timeout, or cancellation.</summary>
    /// <param name="invocation">What to launch.</param>
    /// <param name="cancellationToken">Kills the process tree, then throws once it is reaped.</param>
    /// <returns>The observed result; never throws for a failed or misbehaving ffmpeg.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled; the tree is already dead.</exception>
    Task<FfmpegRunResult> RunAsync(FfmpegInvocation invocation, CancellationToken cancellationToken);
}
