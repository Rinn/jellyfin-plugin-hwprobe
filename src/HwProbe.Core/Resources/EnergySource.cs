namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>An energy counter read through a function.</summary>
/// <param name="Domain">The domain it measures.</param>
/// <param name="WrapJoules">The joules after which it wraps, or null.</param>
/// <param name="Reader">Reads it, in joules, or null when the read fails.</param>
internal sealed record EnergySource(string Domain, double? WrapJoules, Func<double?> Reader) : IEnergySource
{
    /// <inheritdoc/>
    public double? Read() => Reader();
}
