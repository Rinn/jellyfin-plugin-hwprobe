namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>When ffmpeg first and last reported progress, measured from launch, with the frames done by then.</summary>
/// <param name="FirstAt">When the first report with frames done arrived.</param>
/// <param name="FirstFrames">The frames done at the first report.</param>
/// <param name="LastAt">When the last report arrived.</param>
/// <param name="LastFrames">The frames done at the last report.</param>
public sealed record FrameTiming(TimeSpan FirstAt, long FirstFrames, TimeSpan LastAt, long LastFrames)
{
    /// <summary>Gets the frames a second between the first and last reports, leaving out start-up and shutdown, or null when they're too close together to say.</summary>
    public double? SteadyFps => LastAt - FirstAt >= TimeSpan.FromSeconds(1) && LastFrames > FirstFrames
        ? (LastFrames - FirstFrames) / (LastAt - FirstAt).TotalSeconds
        : null;
}
