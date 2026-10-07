namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>How much content a measurement's copies process, and how its speed is read.</summary>
/// <param name="Content">The content the first copy, and each counted copy, processes.</param>
/// <param name="Shortest">A first copy that takes less than this is run again with more content, so start-up doesn't skew its speed.</param>
/// <param name="Target">How long that longer run aims to take.</param>
/// <param name="ByContent">Whether speed is the content over the run's time rather than the frames ffmpeg reports; for outputs with few frames, such as images at an interval.</param>
public sealed record MeterPace(TimeSpan Content, TimeSpan Shortest, TimeSpan Target, bool ByContent)
{
    /// <summary>Gets the pace of a transcode or decode, read from its frames.</summary>
    public static MeterPace Frames { get; } = new(SpeedMeter.Content, TimeSpan.FromSeconds(5), SpeedMeter.Content, false);

    /// <summary>Gets the pace of images at an interval, read from the content, which only a finished run gives: a single copy may take two minutes, so software on a slow CPU still gets a speed.</summary>
    public static MeterPace Images { get; } = Frames with { ByContent = true, SingleTimeout = TimeSpan.FromMinutes(2) };

    /// <summary>Gets the pace of audio, read from the content, which has no frames: a minute of it, as audio runs hundreds of times real time, and a longer run aims for two seconds.</summary>
    public static MeterPace Audio { get; } = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), true);

    /// <summary>Gets how long a single copy may take before it's killed; a transcode cut off still gives fps from the frames it reached.</summary>
    public TimeSpan SingleTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
