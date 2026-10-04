using System.Diagnostics;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads a process's CPU time, peak memory, and GPU engine time from <c>/proc</c>, which is gone once it exits, so it's sampled while it runs.</summary>
internal sealed class LinuxResourceMonitor : SampledResourceMonitor
{
    private readonly string _root;
    private readonly long _ticksPerSecond;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<(string Client, string Engine), long> _nanoseconds = [];
    private readonly Dictionary<(string Client, string Engine), (long Busy, long Total, double At)> _cycles = [];
    private readonly Dictionary<string, double> _cycleSeconds = new(StringComparer.Ordinal);
    private long? _ticks;
    private long? _peak;

    /// <summary>Initializes a new instance of the <see cref="LinuxResourceMonitor"/> class and starts sampling.</summary>
    /// <param name="pid">The process.</param>
    /// <param name="ticksPerSecond">The kernel's clock ticks a second (<c>sysconf(_SC_CLK_TCK)</c>).</param>
    public LinuxResourceMonitor(int pid, long ticksPerSecond)
    {
        _root = $"/proc/{pid}";
        _ticksPerSecond = ticksPerSecond;
        Begin();
    }

    /// <inheritdoc/>
    public override ResourceUsage Finish(double seconds)
    {
        var gpu = new Dictionary<string, double>(_cycleSeconds, StringComparer.Ordinal);

        // A client the process opened starts at zero, so its last reading is all its busy time.
        foreach (var ((_, engine), ns) in _nanoseconds)
        {
            gpu[engine] = gpu.GetValueOrDefault(engine) + (ns / 1e9);
        }

        return new ResourceUsage(seconds, _ticks is { } ticks ? (double)ticks / _ticksPerSecond : null, _peak) { GpuSeconds = gpu.Count > 0 ? gpu : null };
    }

    /// <inheritdoc/>
    protected override void Sample()
    {
        try
        {
            _ticks = ProcFiles.CpuTicks(File.ReadAllText(Path.Combine(_root, "stat"))) ?? _ticks;
            _peak = ProcFiles.PeakBytes(File.ReadAllText(Path.Combine(_root, "status"))) ?? _peak;
            SampleGpu();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The process exited between samples; the last figures stand.
        }
    }

    /// <summary>Adds the engine time of each DRM client the process holds open since the last sample.</summary>
    private void SampleGpu()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(_root, "fdinfo")))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            // Several descriptors can share one client; count each client once.
            if (ProcFiles.Drm(text) is not { } client || !seen.Add(client.Id))
            {
                continue;
            }

            foreach (var (engine, ns) in client.Nanoseconds)
            {
                _nanoseconds[(client.Id, engine)] = ns;
            }

            foreach (var (engine, (busy, total)) in client.Cycles)
            {
                var key = (client.Id, engine);
                if (_cycles.TryGetValue(key, out var last) && total > last.Total)
                {
                    _cycleSeconds[engine] = _cycleSeconds.GetValueOrDefault(engine) + ((double)(busy - last.Busy) / (total - last.Total) * (now - last.At));
                }

                _cycles[key] = (busy, total, now);
            }
        }
    }
}
