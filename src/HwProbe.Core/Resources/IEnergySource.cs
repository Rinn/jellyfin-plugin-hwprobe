namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>A cumulative energy counter for a whole device.</summary>
internal interface IEnergySource
{
    /// <summary>Gets the domain it measures: <c>Cpu</c> for the CPU package, <c>Gpu</c>.</summary>
    string Domain { get; }

    /// <summary>Gets the joules after which the counter wraps to zero, or null when it doesn't.</summary>
    double? WrapJoules { get; }

    /// <summary>Reads the counter.</summary>
    /// <returns>Joules since some fixed point, or null when the read fails.</returns>
    double? Read();
}
