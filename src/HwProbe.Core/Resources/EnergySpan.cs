using System.Diagnostics;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>The energy every meter reports from its start to its stop: counters read at both ends, power meters sampled on a timer in between.</summary>
internal sealed class EnergySpan : IAsyncDisposable
{
    // As often as resources are sampled; amdgpu's power readings change about as fast.
    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(200);

    private readonly IReadOnlyList<IEnergySource> _counters;
    private readonly Dictionary<IEnergySource, double> _start;
    private readonly (IPowerSource Source, PowerIntegral Integral)[] _power;
    private readonly HashSet<IPowerSource> _failed = [];
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    // One sample at a time: timer callbacks can overlap when a read is slow, and the final sample follows the last of them.
    private readonly Lock _gate = new();
    private readonly Timer? _timer;
    private Dictionary<string, double>? _used;
    private bool _stopped;

    /// <summary>Initializes a new instance of the <see cref="EnergySpan"/> class and starts it.</summary>
    /// <param name="counters">The energy counters.</param>
    /// <param name="power">The power meters.</param>
    public EnergySpan(IReadOnlyList<IEnergySource> counters, IReadOnlyList<IPowerSource> power)
    {
        _counters = counters;
        _start = EnergyMeter.Read(counters);
        _power = [.. power.Select(p => (p, new PowerIntegral()))];
        Sample();
        _timer = _power.Length > 0 ? new Timer(_ => Tick(), null, _interval, _interval) : null;
    }

    /// <summary>Gets how long the span ran, once stopped.</summary>
    public double Seconds { get; private set; }

    /// <summary>Stops the span and returns what it measured; later calls return the same.</summary>
    /// <returns>Joules by domain, or null when no meter was read at both ends.</returns>
    public async Task<Dictionary<string, double>?> StopAsync()
    {
        if (_timer is { } timer)
        {
            // Completes once any running callback has returned.
            await timer.DisposeAsync();
        }

        lock (_gate)
        {
            if (_stopped)
            {
                return _used;
            }

            Sample();
            _stopped = true;
            Seconds = Stopwatch.GetElapsedTime(_startedAt).TotalSeconds;
            var used = EnergyMeter.Used(_start, EnergyMeter.Read(_counters)) ?? new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (source, integral) in _power.Where(p => p.Integral.Spans && !_failed.Contains(p.Source)))
            {
                used[source.Domain] = used.GetValueOrDefault(source.Domain) + integral.Joules;
            }

            _used = used.Count > 0 ? used : null;
            return _used;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>Takes one sample from the timer, unless one is already running or the span has stopped.</summary>
    private void Tick()
    {
        if (!_gate.TryEnter())
        {
            return;
        }

        try
        {
            if (!_stopped)
            {
                Sample();
            }
        }
        finally
        {
            _gate.Exit();
        }
    }

    /// <summary>Reads every power meter once; a read that fails is skipped, the samples either side of it joined, and a meter that throws is left out of the span.</summary>
    private void Sample()
    {
        var seconds = Stopwatch.GetElapsedTime(_startedAt).TotalSeconds;
        foreach (var (source, integral) in _power)
        {
            try
            {
                if (source.ReadWatts() is { } watts)
                {
                    integral.Add(seconds, watts);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // An exception escaping a timer callback would end the server's process.
                _failed.Add(source);
            }
        }
    }
}
