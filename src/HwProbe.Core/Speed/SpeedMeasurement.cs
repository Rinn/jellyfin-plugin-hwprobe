namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What measuring one command found.</summary>
/// <param name="Fps">Frames per second of one copy run flat out, or null when it produced none.</param>
/// <param name="Streams">How many copies keep real time at once, or null when not counted.</param>
/// <param name="Capped">Whether <see cref="Streams"/> stopped at <see cref="SpeedMeter.MaxStreams"/>.</param>
/// <param name="Note">Why a value is missing, or null.</param>
public sealed record SpeedMeasurement(double? Fps, int? Streams, bool Capped, string? Note)
{
    /// <summary>Gets a value indicating whether a run was cut off by a timeout or the time limit, so the figures may be short of a full measurement.</summary>
    public bool Interrupted { get; init; }
}
