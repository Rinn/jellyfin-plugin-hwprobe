namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>One saved measurement, with what it was measured with.</summary>
/// <param name="MeasuredUtc">When it was measured.</param>
/// <param name="Method">The <see cref="SpeedResultCache.MeasurementVersion"/> that measured it.</param>
/// <param name="FfmpegVersion">The ffmpeg version line it was measured with.</param>
/// <param name="Result">The result.</param>
public sealed record SpeedCacheEntry(DateTimeOffset MeasuredUtc, int Method, string FfmpegVersion, SpeedResult Result);
