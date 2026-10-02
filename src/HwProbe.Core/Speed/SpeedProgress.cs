namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>One finished measurement in a running speed run.</summary>
/// <param name="Done">Measurements finished.</param>
/// <param name="Total">Measurements in the run.</param>
/// <param name="Result">The one just finished.</param>
public sealed record SpeedProgress(int Done, int Total, SpeedResult Result);
