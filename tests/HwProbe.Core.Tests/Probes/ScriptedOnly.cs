using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Answers exact argument strings with stdout; everything else exits 1 with <see cref="OtherStderr"/>.</summary>
/// <param name="stdoutByArguments">Stdout per exact argument string.</param>
internal sealed class ScriptedOnly(Dictionary<string, string> stdoutByArguments) : IFfmpegRunner
{
    /// <summary>Gets or sets stderr for unscripted launches.</summary>
    public string OtherStderr { get; set; } = string.Empty;

    /// <summary>Gets every launch's arguments, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <inheritdoc/>
    public Task<FfmpegRunResult> RunAsync(FfmpegInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        Calls.Add(invocation.Arguments);
        var found = stdoutByArguments.TryGetValue(invocation.Arguments, out var stdout);
        return Task.FromResult(new FfmpegRunResult(FfmpegRunStatus.Exited, found ? 0 : 1, stdout ?? string.Empty, found ? string.Empty : OtherStderr, null, TimeSpan.Zero, null));
    }
}
