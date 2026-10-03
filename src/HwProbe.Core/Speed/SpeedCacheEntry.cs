namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>One saved measurement, with what it was measured with.</summary>
/// <param name="MeasuredUtc">When it was measured.</param>
/// <param name="HwProbeVersion">The version that measured it.</param>
/// <param name="FfmpegVersion">The ffmpeg version line it was measured with.</param>
/// <param name="Result">The result.</param>
public sealed record SpeedCacheEntry(DateTimeOffset MeasuredUtc, string HwProbeVersion, string FfmpegVersion, SpeedResult Result);
