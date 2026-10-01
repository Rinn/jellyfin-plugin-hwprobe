using Jellyfin.Plugin.HwProbe.Core.Fixtures;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>One probe cell: which report column it fills, its fixture, and the job to synthesise.</summary>
/// <param name="Group">Report column.</param>
/// <param name="Key">Cell key within the column, e.g. <c>hevc10</c>.</param>
/// <param name="Fixture">Fixture decoded as input.</param>
/// <param name="Cell">The probe cell handed to the argument source.</param>
public sealed record MatrixCell(MatrixGroup Group, string Key, FixtureSpec Fixture, ProbeCell Cell)
{
    /// <summary>Gets a subtitle fixture to burn in, or null for none.</summary>
    public FixtureSpec? SubtitleFixture { get; init; }
}
