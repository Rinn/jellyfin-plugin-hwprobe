namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>The probed ffmpeg binary.</summary>
/// <param name="Path">Absolute path.</param>
/// <param name="Source">How it was found: CommandLine, EnvironmentVariable, KnownPath or SystemPath.</param>
/// <param name="Version">Parsed version.</param>
/// <param name="IsJellyfinBuild">Whether the banner identifies jellyfin-ffmpeg.</param>
public sealed record FfmpegSummary(string Path, string Source, string Version, bool IsJellyfinBuild);
