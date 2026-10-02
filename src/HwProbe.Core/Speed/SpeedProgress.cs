namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>A running speed run's progress: the test videos are ready, or one more measurement finished.</summary>
/// <param name="Done">Measurements finished.</param>
/// <param name="Total">Measurements in the run.</param>
/// <param name="Result">The one just finished, or null when the test videos are ready and measuring starts.</param>
public sealed record SpeedProgress(int Done, int Total, SpeedResult? Result);
