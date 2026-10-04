using System.CommandLine;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>The hwprobe command-line entry point.</summary>
internal static class Program
{
    /// <summary>Parses arguments and runs a probe.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A <see cref="HwProbeExitCode"/> value.</returns>
    private static async Task<int> Main(string[] args)
    {
        StopReason.Watch();
        var command = new HwProbeCommand();
        command.Root.SetAction((parse, ct) => HwProbeApp.RunAsync(command.Bind(parse), ct));

        var parse = command.Root.Parse(args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors)
            {
                await Console.Error.WriteLineAsync(error.Message);
            }

            return (int)HwProbeExitCode.UsageError;
        }

        try
        {
            var code = await parse.InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null }, StopReason.Token);
            return StopReason.ExitCode ?? code;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync($"hwprobe: internal error: {ex}");
            return (int)HwProbeExitCode.InternalError;
        }
    }
}
