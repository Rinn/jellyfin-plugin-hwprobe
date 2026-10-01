using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>What one engine run probes and where it caches.</summary>
/// <param name="Ffmpeg">The binary under test.</param>
/// <param name="StopAfter">The last stage to run.</param>
/// <param name="Types">Backends to probe; empty means all.</param>
/// <param name="Device">Restrict to one device, or null for all.</param>
/// <param name="ProbeTimeout">Per-probe hard timeout.</param>
/// <param name="FixtureTimeout">Per-fixture generation timeout.</param>
/// <param name="FixturesDirectory">Fixture cache root.</param>
/// <param name="ReportCacheDirectory">Report cache root.</param>
/// <param name="Refresh">Ignore a cached report.</param>
public sealed record EngineOptions(
    FfmpegLocation Ffmpeg,
    StopStage StopAfter,
    IReadOnlySet<HwType> Types,
    string? Device,
    TimeSpan ProbeTimeout,
    TimeSpan FixtureTimeout,
    string FixturesDirectory,
    string ReportCacheDirectory,
    bool Refresh);
