namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>How the server extracts images at an interval for one width (MediaEncoder.ExtractVideoImagesOnIntervalAccelerated), as trickplay does from its TrickplayOptions.</summary>
/// <param name="Width">The images' width.</param>
/// <param name="IntervalMilliseconds">The time between images.</param>
/// <param name="Qscale">The quality scale, 2 (best) to 31.</param>
/// <param name="Threads">The threads ffmpeg is given; 0 lets it pick.</param>
/// <param name="HwEncoding">Whether the backend's MJPEG encoder is used where it has one.</param>
/// <param name="KeyFramesOnly">Whether only key frames are decoded.</param>
public sealed record ImageJob(int Width, int IntervalMilliseconds, int Qscale, int Threads, bool HwEncoding, bool KeyFramesOnly);
