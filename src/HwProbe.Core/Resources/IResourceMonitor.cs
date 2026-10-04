namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Follows one process while it runs and reports what it used.</summary>
public interface IResourceMonitor : IDisposable
{
    /// <summary>Stops following the process once it has exited.</summary>
    /// <returns>A task that completes when the last sample is taken.</returns>
    Task StopAsync();

    /// <summary>Returns the figures once the monitor has stopped.</summary>
    /// <param name="seconds">Wall-clock time the run took.</param>
    /// <returns>The usage.</returns>
    ResourceUsage Finish(double seconds);
}
