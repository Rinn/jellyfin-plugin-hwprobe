using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Scheduled task that runs a probe; no default trigger.</summary>
/// <param name="service">The probe runner.</param>
public sealed class HardwareProbeTask(ProbeService service) : IScheduledTask
{
    /// <inheritdoc/>
    public string Name => "Probe hardware transcoding";

    /// <inheritdoc/>
    public string Key => "HwProbeHardwareProbe";

    /// <inheritdoc/>
    public string Description => "Tests which hardware transcoding backends and codecs actually work. Run while nothing is playing.";

    /// <inheritdoc/>
    public string Category => "HwProbe";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var result = await service.RunAsync(cancellationToken);
        progress.Report(100);

        // Surface failures in the dashboard's task history rather than reporting success.
        if (result is ProbeRunResult.Failed or ProbeRunResult.ServerBusy or ProbeRunResult.AlreadyRunning)
        {
            throw new InvalidOperationException(result == ProbeRunResult.Failed ? service.Status.LastError : $"Probe not run: {result}.");
        }
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
