using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Puts a process in a job object, whose accounting covers its whole tree and outlives it, and samples its GPU engines' running time, which ends with it.</summary>
[SupportedOSPlatform("windows5.1.2600")]
internal sealed class WindowsResourceMonitor : SampledResourceMonitor
{
    private readonly JobObject? _job;
    private readonly int _pid;
    private readonly Dictionary<string, long> _engines = new(StringComparer.Ordinal);
    private (nint Query, nint Engines, nint Memory)? _gpu;
    private long? _gpuPeak;

    /// <summary>Initializes a new instance of the <see cref="WindowsResourceMonitor"/> class and starts sampling.</summary>
    /// <param name="process">The started process.</param>
    public WindowsResourceMonitor(Process process)
    {
        _pid = process.Id;
        var job = new JobObject();
        try
        {
            job.AssignProcess(process);
            _job = job;
        }
        catch (Win32Exception)
        {
            // The process already exited, or a parent job refuses nesting: no CPU or memory figures, the run goes on.
            job.Dispose();
        }

        _gpu = WindowsNativeMethods.OpenGpuQuery();
        Begin();
    }

    /// <inheritdoc/>
    public override ResourceUsage Finish(double seconds)
    {
        double? cpu = null;
        long? peak = null;
        if (_job is { } job)
        {
            try
            {
                var basic = job.GetBasicAccountingInformation();
                cpu = (basic.TotalUserTime + basic.TotalKernelTime).TotalSeconds;
                peak = (long)job.GetMemoryAccountingInformation().PeakJobMemoryUsed;
            }
            catch (Win32Exception)
            {
                // Leaves the figures out rather than failing the measurement.
            }
        }

        // Running Time is in 100-nanosecond units and starts at zero with the process. An engine type can have several
        // instances (an RTX 5080 has two NVDEC engines); the busiest stands for the type, so the share stays within 100%.
        var gpu = Failed ? [] : _engines
            .Where(e => GpuEngineCounters.Parse(e.Key) is not null)
            .GroupBy(e => GpuEngineCounters.Parse(e.Key)!.Value.Engine, StringComparer.Ordinal)
            .Where(g => g.Any(e => e.Value > 0))
            .ToDictionary(g => g.Key, g => g.Max(e => e.Value / 1e7), StringComparer.Ordinal);
        return new ResourceUsage(seconds, cpu, peak) { GpuSeconds = gpu.Count > 0 ? gpu : null, PeakGpuMemoryBytes = Failed ? null : _gpuPeak };
    }

    /// <inheritdoc/>
    protected override void Sample()
    {
        if (_gpu is not { } gpu)
        {
            return;
        }

        if (!WindowsNativeMethods.Collect(gpu.Query))
        {
            return;
        }

        foreach (var (instance, time) in WindowsNativeMethods.ReadRaw(gpu.Engines))
        {
            if (GpuEngineCounters.Parse(instance)?.Pid == _pid)
            {
                _engines[instance] = time;
            }
        }

        // One instance per adapter the process uses.
        var memory = WindowsNativeMethods.ReadRaw(gpu.Memory).Where(m => GpuEngineCounters.Pid(m.Key) == _pid).Sum(m => m.Value);
        if (memory > 0)
        {
            _gpuPeak = Math.Max(_gpuPeak ?? 0, memory);
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        // The base waits for a running sample, so the query isn't closed under it.
        base.Dispose(disposing);
        if (disposing)
        {
            _job?.Dispose();
        }

        if (_gpu is { } gpu)
        {
            WindowsNativeMethods.CloseQuery(gpu.Query);
            _gpu = null;
        }
    }
}
