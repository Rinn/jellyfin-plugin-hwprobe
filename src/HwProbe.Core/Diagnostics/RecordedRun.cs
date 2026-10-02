using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.Core.Diagnostics;

/// <summary>One ffmpeg launch and everything it produced.</summary>
/// <param name="Invocation">What was launched.</param>
/// <param name="Result">What it did, with complete stdout and stderr.</param>
public sealed record RecordedRun(FfmpegInvocation Invocation, FfmpegRunResult Result);
