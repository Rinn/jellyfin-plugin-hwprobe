namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads a process's CPU time and peak memory with <c>proc_pid_rusage</c>, sampled while it runs since an exited process can't be asked.</summary>
internal sealed class MacResourceMonitor : SampledResourceMonitor
{
    private readonly int _pid;
    private ulong? _cpu;
    private ulong? _peak;

    /// <summary>Initializes a new instance of the <see cref="MacResourceMonitor"/> class and starts sampling.</summary>
    /// <param name="pid">The process.</param>
    public MacResourceMonitor(int pid)
    {
        _pid = pid;
        Begin();
    }

    /// <inheritdoc/>
    public override ResourceUsage Finish(double seconds) =>
        Failed ? new(seconds, null, null) : new(seconds, _cpu is { } cpu ? NativeMethods.MachSeconds(cpu) : null, _peak is { } peak ? (long)peak : null);

    /// <inheritdoc/>
    protected override void Sample()
    {
        if (NativeMethods.ProcPidRusage(_pid) is { } usage)
        {
            (_cpu, _peak) = (usage.CpuTime, usage.LifetimeMaxFootprint);
        }
    }
}
