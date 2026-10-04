using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Remembers which signal asked the process to stop, so a cut-short run can say why.</summary>
internal static class StopReason
{
    private static readonly List<PosixSignalRegistration> _registrations = [];
    private static PosixSignal? _received;

    /// <summary>Starts noting stop signals; each still stops the process as it otherwise would.</summary>
    public static void Watch()
    {
        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGQUIT })
        {
            try
            {
                _registrations.Add(PosixSignalRegistration.Create(signal, context => _received ??= context.Signal));
            }
            catch (PlatformNotSupportedException)
            {
                // Windows has no SIGHUP or SIGQUIT.
            }
        }
    }

    /// <summary>Describes why the run stopped.</summary>
    /// <returns>The signal received, or that nothing in this process asked it to stop.</returns>
    public static string Describe() => _received is { } signal ? $"the process received {signal}" : "no stop signal was received";
}
