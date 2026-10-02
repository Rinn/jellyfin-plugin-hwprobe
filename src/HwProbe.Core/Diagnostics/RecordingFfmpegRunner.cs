using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.Core.Diagnostics;

/// <summary>Passes launches to another runner and keeps every one, for a diagnostics bundle.</summary>
/// <param name="inner">The runner that launches ffmpeg.</param>
public sealed class RecordingFfmpegRunner(IFfmpegRunner inner) : IFfmpegRunner
{
    private readonly List<RecordedRun> _runs = [];

    /// <summary>Gets a copy of the launches so far, in order.</summary>
    public IReadOnlyList<RecordedRun> Runs
    {
        get
        {
            lock (_runs)
            {
                return [.. _runs];
            }
        }
    }

    /// <inheritdoc/>
    public async Task<FfmpegRunResult> RunAsync(FfmpegInvocation invocation, CancellationToken cancellationToken)
    {
        var result = await inner.RunAsync(invocation, cancellationToken);
        lock (_runs)
        {
            _runs.Add(new RecordedRun(invocation, result));
        }

        return result;
    }
}
