namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>One ffmpeg launch: binary, argument string, environment overrides and hard timeout.</summary>
/// <param name="ExecutablePath">Absolute path to the ffmpeg binary.</param>
/// <param name="Arguments">The argument string, quoted as <c>EncodingHelper</c> emits it.</param>
/// <param name="Environment">Variables to set in the child; a null value removes the variable.</param>
/// <param name="Timeout">Hard limit after which the whole process tree is killed.</param>
public sealed record FfmpegInvocation(
    string ExecutablePath,
    string Arguments,
    IReadOnlyDictionary<string, string?> Environment,
    TimeSpan Timeout);
