using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Parsed command-line options.</summary>
/// <param name="FfmpegPath">The <c>--ffmpeg</c> path, or null to auto-discover.</param>
/// <param name="StopAfter">The last stage to run.</param>
/// <param name="Types">Backends to probe; empty means all.</param>
/// <param name="Device">Restrict to one device node or adapter index, or null for all.</param>
/// <param name="Format">Stdout rendering.</param>
/// <param name="JsonPath">Also write the JSON report here, or null.</param>
/// <param name="ProbeTimeout">Per-probe hard timeout.</param>
/// <param name="FixtureTimeout">Per-fixture generation timeout.</param>
/// <param name="Refresh">Ignore cached results.</param>
/// <param name="FixturesDirectory">Fixture cache directory.</param>
/// <param name="ExpectHardware">Exit 1 when no backend is viable.</param>
/// <param name="Verbose">Echo command lines and stderr tails.</param>
internal sealed record CliOptions(
    string? FfmpegPath,
    StopStage StopAfter,
    IReadOnlySet<HwType> Types,
    string? Device,
    OutputFormat Format,
    string? JsonPath,
    TimeSpan ProbeTimeout,
    TimeSpan FixtureTimeout,
    bool Refresh,
    string FixturesDirectory,
    bool ExpectHardware,
    bool Verbose)
{
    /// <summary>Gets where to write a diagnostics zip, or null for none.</summary>
    public string? DiagnosticsPath { get; init; }
}
