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
    public string Description => "Tests which hardware transcoding options work on this server. Run while nothing is playing.";

    /// <inheritdoc/>
    public string Category => "HwProbe";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var result = await service.RunAsync(cancellationToken);
        progress.Report(100);

        // A busy server or a probe already running is a skip, logged by the service; only a failed probe fails the task.
        if (result == ProbeRunResult.Failed)
        {
            throw new InvalidOperationException(service.Status.LastError);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
