namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>A whole-device meter that reports power rather than a cumulative energy counter, so its energy is summed over samples.</summary>
internal interface IPowerSource
{
    /// <summary>Gets the domain it measures, as <see cref="IEnergySource.Domain"/> names it.</summary>
    string Domain { get; }

    /// <summary>Reads the meter.</summary>
    /// <returns>Watts, or null when the read fails.</returns>
    double? ReadWatts();
}
