using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Stops a run on the first stop signal, so it can print what it finished and say why it stopped.</summary>
/// <remarks>System.CommandLine's own handling gives a run two seconds before ending the process, too short to stop ffmpeg and print a speed test's results.</remarks>
internal static class StopReason
{
    private static readonly List<PosixSignalRegistration> _registrations = [];
    private static readonly CancellationTokenSource _stop = new();
    private static int _received;

    /// <summary>Gets the token cancelled by the first stop signal.</summary>
    public static CancellationToken Token => _stop.Token;

    /// <summary>Gets the exit code for a run a signal stopped, 128 plus the signal's number as shells report it, or null.</summary>
    public static int? ExitCode => Received switch
    {
        PosixSignal.SIGHUP => 129,
        PosixSignal.SIGINT => 130,
        PosixSignal.SIGQUIT => 131,
        PosixSignal.SIGTERM => 143,
        _ => null,
    };

    /// <summary>Gets the first stop signal received, or null.</summary>
    private static PosixSignal? Received => Volatile.Read(ref _received) is var signal and not 0 ? (PosixSignal)signal : null;

    /// <summary>Starts handling stop signals: the first cancels <see cref="Token"/>, and a second ends the process as it otherwise would.</summary>
    public static void Watch()
    {
        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGQUIT })
        {
            try
            {
                _registrations.Add(PosixSignalRegistration.Create(signal, OnSignal));
            }
            catch (PlatformNotSupportedException)
            {
                // Windows has no SIGHUP or SIGQUIT.
            }
        }
    }

    /// <summary>Describes why the run stopped.</summary>
    /// <returns>The signal received, or that nothing in this process asked it to stop.</returns>
    public static string Describe() => Received is { } signal ? $"the process received {signal}" : "no stop signal was received";

    /// <summary>Handles a stop signal.</summary>
    /// <param name="context">The signal.</param>
    private static void OnSignal(PosixSignalContext context)
    {
        // PosixSignal values are negative, so 0 means none yet.
        if (Interlocked.CompareExchange(ref _received, (int)context.Signal, 0) != 0)
        {
            return;
        }

        context.Cancel = true;
        _stop.Cancel();
    }
}
