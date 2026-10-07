namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>A power meter read through a function.</summary>
/// <param name="Domain">The domain it measures.</param>
/// <param name="Reader">Reads it, in watts, or null when the read fails.</param>
internal sealed record PowerSource(string Domain, Func<double?> Reader) : IPowerSource
{
    /// <inheritdoc/>
    public double? ReadWatts() => Reader();
}
