namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>A resolved ffmpeg binary and how it was found.</summary>
/// <param name="Path">Absolute path to the binary.</param>
/// <param name="Source">Which discovery step found it.</param>
public sealed record FfmpegLocation(string Path, FfmpegSource Source);
