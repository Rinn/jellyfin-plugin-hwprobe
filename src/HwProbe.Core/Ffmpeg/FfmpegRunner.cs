using System.ComponentModel;
using System.Diagnostics;

namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Launches ffmpeg, captures its output, and guarantees the process tree is reaped.</summary>
/// <remarks>
/// Not a serialization point: callers that mutate process environment around a launch (as
/// <c>EncodingHelper</c> does) must hold their own lock across generate → run → restore.
/// </remarks>
public sealed class FfmpegRunner : IFfmpegRunner
{
    // Pipes still open after this mean a descendant escaped the tree kill.
    private static readonly TimeSpan _drainGrace = TimeSpan.FromSeconds(5);

    /// <summary>Gets the variables every launch starts from, before the invocation's own.</summary>
    public static IReadOnlyDictionary<string, string> BaseEnvironment { get; } = new Dictionary<string, string>(StringComparer.Ordinal) { ["LC_ALL"] = "C" };

    /// <inheritdoc/>
    public async Task<FfmpegRunResult> RunAsync(FfmpegInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        using var process = new Process { StartInfo = CreateStartInfo(invocation) };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new FfmpegRunResult(FfmpegRunStatus.LaunchFailed, null, string.Empty, string.Empty, null, stopwatch.Elapsed, ex.Message);
        }

        // Closed stdin stops ffmpeg waiting for interactive keys.
        process.StandardInput.Close();
        LowerPriority(process);

        // Drain both streams concurrently, or >1 MB of output deadlocks (jellyfin#17429).
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var drained = Task.WhenAll(stdoutTask, stderrTask);

        var status = FfmpegRunStatus.Exited;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(invocation.Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                status = FfmpegRunStatus.TimedOut;
            }
        }

        if (status == FfmpegRunStatus.TimedOut)
        {
            // A leaked ffmpeg holding the render node breaks later probes.
            KillTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        try
        {
            await drained.WaitAsync(_drainGrace, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            // A descendant escaped the kill and holds the pipes; the output is lost.
            KillTree(process);
            status = FfmpegRunStatus.TimedOut;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var stdout = drained.IsCompletedSuccessfully ? await stdoutTask : string.Empty;
        var stderr = drained.IsCompletedSuccessfully ? await stderrTask : string.Empty;
        var exitCode = status == FfmpegRunStatus.Exited ? process.ExitCode : (int?)null;
        return new FfmpegRunResult(status, exitCode, stdout, stderr, ProgressParser.LastFrame(stdout), stopwatch.Elapsed, null);
    }

    /// <summary>Builds start info with redirected stdio and the invocation's environment overrides.</summary>
    /// <param name="invocation">The invocation to launch.</param>
    /// <returns>The start info.</returns>
    internal static ProcessStartInfo CreateStartInfo(FfmpegInvocation invocation)
    {
        var info = new ProcessStartInfo(invocation.ExecutablePath, invocation.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // ffmpeg writes UTF-8 to a pipe on every platform; the console code page (850 on a Windows PC tested) doesn't apply.
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        // Verdicts match stderr text such as strerror's "Permission denied" and parse numbers with dots; the server's
        // locale could change both in libraries ffmpeg loads (drivers, OpenCL). An invocation can still override it.
        foreach (var (name, value) in BaseEnvironment)
        {
            info.Environment[name] = value;
        }

        foreach (var (name, value) in invocation.Environment)
        {
            if (value is null)
            {
                info.Environment.Remove(name);
            }
            else
            {
                info.Environment[name] = value;
            }
        }

        return info;
    }

    /// <summary>Drops the child to below-normal priority so probing doesn't starve the host.</summary>
    /// <param name="process">The started process.</param>
    private static void LowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Not permitted; normal priority is fine.
        }
    }

    /// <summary>Kills the process and every descendant, tolerating one that already exited.</summary>
    /// <param name="process">The root process.</param>
    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
