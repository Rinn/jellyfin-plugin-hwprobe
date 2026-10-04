using System.Diagnostics;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Puts a process in a job object, whose accounting covers its whole tree and outlives it, and samples its GPU engines' running time, which ends with it.</summary>
[SupportedOSPlatform("windows5.1.2600")]
internal sealed class WindowsResourceMonitor : SampledResourceMonitor
{
    private readonly JobObject _job = new();
    private readonly int _pid;
    private readonly (nint Query, nint Counter)? _gpu;
    private readonly Dictionary<string, long> _engines = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="WindowsResourceMonitor"/> class and starts sampling.</summary>
    /// <param name="process">The started process.</param>
    public WindowsResourceMonitor(Process process)
    {
        _job.AssignProcess(process);
        _pid = process.Id;
        _gpu = WindowsNativeMethods.OpenGpuQuery();
        Begin();
    }

    /// <inheritdoc/>
    public override ResourceUsage Finish(double seconds)
    {
        var basic = _job.GetBasicAccountingInformation();
        var memory = _job.GetMemoryAccountingInformation();

        // Running Time is in 100-nanosecond units and starts at zero with the process; an engine type can have several instances (one per adapter or engine).
        var gpu = _engines
            .Where(e => GpuEngineCounters.Parse(e.Key) is not null)
            .GroupBy(e => GpuEngineCounters.Parse(e.Key)!.Value.Engine, StringComparer.Ordinal)
            .Where(g => g.Any(e => e.Value > 0))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Value / 1e7), StringComparer.Ordinal);
        return new ResourceUsage(seconds, (basic.TotalUserTime + basic.TotalKernelTime).TotalSeconds, (long)memory.PeakJobMemoryUsed) { GpuSeconds = gpu.Count > 0 ? gpu : null };
    }

    /// <inheritdoc/>
    protected override void Sample()
    {
        if (_gpu is not { } gpu)
        {
            return;
        }

        foreach (var (instance, time) in WindowsNativeMethods.ReadRunningTimes(gpu.Query, gpu.Counter))
        {
            if (GpuEngineCounters.Parse(instance)?.Pid == _pid)
            {
                _engines[instance] = time;
            }
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _job.Dispose();
        }

        if (_gpu is { } gpu)
        {
            WindowsNativeMethods.CloseQuery(gpu.Query);
        }
    }
}
