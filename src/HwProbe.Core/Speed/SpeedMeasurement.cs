namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What measuring one command found.</summary>
/// <param name="Fps">Frames per second of one copy run flat out, or null when it produced none.</param>
/// <param name="Streams">How many copies keep real time at once, or null when not counted.</param>
/// <param name="Capped">Whether <see cref="Streams"/> stopped at <see cref="SpeedMeter.MaxStreams"/>, a driver's session limit, or the memory or CPU free, so at least that many keep up.</param>
/// <param name="Note">Why a value is missing, or null.</param>
public sealed record SpeedMeasurement(double? Fps, int? Streams, bool Capped, string? Note)
{
    /// <summary>Gets a value indicating whether a run was cut off by a timeout or the time limit, so the figures may be short of a full measurement.</summary>
    public bool Interrupted { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Streams"/> stopped at the memory or CPU free at the time.</summary>
    public bool HostLimited { get; init; }

    /// <summary>Gets what the single copy whose speed is reported used, or null when not measured.</summary>
    public Resources.ResourceUsage? Resources { get; init; }
}
