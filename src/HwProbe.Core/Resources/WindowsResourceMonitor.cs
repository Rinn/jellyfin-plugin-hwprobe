using System.Diagnostics;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Puts a process in a job object, whose accounting covers its whole tree and outlives it.</summary>
[SupportedOSPlatform("windows5.1.2600")]
internal sealed class WindowsResourceMonitor : IResourceMonitor
{
    private readonly JobObject _job = new();

    /// <summary>Initializes a new instance of the <see cref="WindowsResourceMonitor"/> class.</summary>
    /// <param name="process">The started process.</param>
    public WindowsResourceMonitor(Process process) => _job.AssignProcess(process);

    /// <inheritdoc/>
    public Task StopAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public ResourceUsage Finish(double seconds)
    {
        var basic = _job.GetBasicAccountingInformation();
        var memory = _job.GetMemoryAccountingInformation();
        return new ResourceUsage(seconds, (basic.TotalUserTime + basic.TotalKernelTime).TotalSeconds, (long)memory.PeakJobMemoryUsed);
    }

    /// <inheritdoc/>
    public void Dispose() => _job.Dispose();
}
