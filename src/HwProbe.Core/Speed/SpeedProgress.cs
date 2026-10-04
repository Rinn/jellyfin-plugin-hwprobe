namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>A running speed run's progress: its plan, a test video being made or downloaded, measuring starting, or one more measurement finished.</summary>
/// <param name="Done">Measurements finished.</param>
/// <param name="Total">Measurements in the run.</param>
/// <param name="Result">The one just finished, or null.</param>
public sealed record SpeedProgress(int Done, int Total, SpeedResult? Result)
{
    /// <summary>Gets every planned measurement, described and <see cref="SpeedResult.Pending"/>, in the order they run; only in the first report.</summary>
    public IReadOnlyList<SpeedResult>? Planned { get; init; }

    /// <summary>Gets what's being prepared, e.g. <c>Downloading Animation: 5 of 14 MB</c>, or null once measuring starts.</summary>
    public string? Preparing { get; init; }

    /// <summary>Gets the test videos made or found so far; all of them once measuring starts.</summary>
    public int ClipsDone { get; init; }

    /// <summary>Gets the test videos the run needs, which count toward its progress before the measurements.</summary>
    public int ClipsTotal { get; init; }
}
