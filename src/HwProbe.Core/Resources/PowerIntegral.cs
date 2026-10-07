namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>The energy under a series of power samples, each joined to the next by a straight line.</summary>
internal sealed class PowerIntegral
{
    private double? _lastSeconds;
    private double _lastWatts;

    /// <summary>Gets the joules between the first sample and the last.</summary>
    public double Joules { get; private set; }

    /// <summary>Gets a value indicating whether two samples or more were added, so <see cref="Joules"/> covers a span of time.</summary>
    public bool Spans { get; private set; }

    /// <summary>Adds a sample.</summary>
    /// <param name="seconds">When it was taken, in seconds from any fixed point; later than the one before.</param>
    /// <param name="watts">The power.</param>
    public void Add(double seconds, double watts)
    {
        if (_lastSeconds is { } last)
        {
            Joules += (_lastWatts + watts) / 2 * (seconds - last);
            Spans = true;
        }

        _lastSeconds = seconds;
        _lastWatts = watts;
    }
}
