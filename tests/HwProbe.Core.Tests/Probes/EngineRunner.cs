using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.TestSupport;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Replays recorded build enumeration output, writes fixture files, and scripts every other launch.</summary>
internal sealed class EngineRunner : IFfmpegRunner
{
    private readonly ScriptedFfmpegRunner _stageA = ScriptedFfmpegRunner.FromCorpus("homebrew-9.0.2-macos");

    /// <summary>Gets or sets the response for device-open and probe launches.</summary>
    public Func<FfmpegInvocation, FfmpegRunResult> Probe { get; set; } = _ => Exited(0, 10, string.Empty);

    /// <summary>Gets every launch's arguments, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Gets every launch, for asserting environment overrides.</summary>
    public List<FfmpegInvocation> Invocations { get; } = [];

    /// <summary>Builds an exited result.</summary>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="frames">The final frame count.</param>
    /// <param name="stderr">Captured stderr.</param>
    /// <returns>The result.</returns>
    public static FfmpegRunResult Exited(int exitCode, long? frames, string stderr) =>
        new(FfmpegRunStatus.Exited, exitCode, string.Empty, stderr, frames, TimeSpan.FromMilliseconds(5), null);

    /// <inheritdoc/>
    public async Task<FfmpegRunResult> RunAsync(FfmpegInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        Calls.Add(invocation.Arguments);
        Invocations.Add(invocation);

        if (invocation.Arguments.Contains("-loglevel error -y", StringComparison.Ordinal))
        {
            // Fixture encode: the output path is the last argument.
            var output = invocation.Arguments.Split(' ')[^1].Trim('"');
            await File.WriteAllTextAsync(output, "fixture", cancellationToken);
            return Exited(0, 25, string.Empty);
        }

        if (invocation.Arguments.Contains("-init_hw_device", StringComparison.Ordinal))
        {
            return Probe(invocation);
        }

        return await _stageA.RunAsync(invocation, cancellationToken);
    }
}
