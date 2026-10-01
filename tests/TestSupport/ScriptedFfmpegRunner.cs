using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.TestSupport;

/// <summary>An <see cref="IFfmpegRunner"/> that returns canned stdout keyed by the argument string.</summary>
internal sealed class ScriptedFfmpegRunner : IFfmpegRunner
{
    private readonly Dictionary<string, string> _stdoutByArguments;

    /// <summary>Initializes a new instance of the <see cref="ScriptedFfmpegRunner"/> class.</summary>
    /// <param name="stdoutByArguments">Stdout per exact argument string; unknown arguments exit 1 silently.</param>
    public ScriptedFfmpegRunner(Dictionary<string, string> stdoutByArguments)
    {
        _stdoutByArguments = stdoutByArguments;
    }

    /// <summary>Gets every argument string run, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Builds a runner from a recorded corpus directory.</summary>
    /// <param name="directory">Directory under <c>Corpus/ffmpeg/</c>.</param>
    /// <returns>The runner.</returns>
    public static ScriptedFfmpegRunner FromCorpus(string directory)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Corpus", "ffmpeg", directory);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.txt"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var arguments = name.StartsWith("h-filter-", StringComparison.Ordinal)
                ? "-h filter=" + name["h-filter-".Length..]
                : "-" + name;
            map[arguments] = File.ReadAllText(file);
        }

        return new ScriptedFfmpegRunner(map);
    }

    /// <inheritdoc/>
    public Task<FfmpegRunResult> RunAsync(FfmpegInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        Calls.Add(invocation.Arguments);
        var found = _stdoutByArguments.TryGetValue(invocation.Arguments, out var stdout);
        return Task.FromResult(new FfmpegRunResult(
            FfmpegRunStatus.Exited, found ? 0 : 1, stdout ?? string.Empty, string.Empty, null, TimeSpan.Zero, null));
    }
}
