namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>How much content a measurement's copies process, and how its speed is read.</summary>
/// <param name="Content">The content the first copy, and each counted copy, processes.</param>
/// <param name="Shortest">A first copy that takes less than this is run again with more content, so start-up doesn't skew its speed.</param>
/// <param name="Target">How long that longer run aims to take.</param>
/// <param name="ByContent">Whether speed is the content over the run's time rather than the frames ffmpeg reports; for outputs without frames, such as audio.</param>
public sealed record MeterPace(TimeSpan Content, TimeSpan Shortest, TimeSpan Target, bool ByContent)
{
    /// <summary>Gets the pace of a transcode or decode, read from its frames.</summary>
    public static MeterPace Frames { get; } = new(SpeedMeter.Content, TimeSpan.FromSeconds(5), SpeedMeter.Content, false);

    /// <summary>Gets the pace of audio, read from the content, which has no frames: a minute of it, as audio runs hundreds of times real time, and a longer run aims for two seconds.</summary>
    public static MeterPace Audio { get; } = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), true);
}
