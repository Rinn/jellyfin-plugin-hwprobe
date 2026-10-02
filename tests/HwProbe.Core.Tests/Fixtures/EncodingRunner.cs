using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>A fake runner that "encodes" by writing bytes to the quoted output path at the end of the arguments.</summary>
internal sealed class EncodingRunner : IFfmpegRunner
{
    /// <summary>Gets the invocations seen, in order.</summary>
    public List<FfmpegInvocation> Invocations { get; } = [];

    /// <summary>Gets or sets the exit code returned; non-zero writes no output.</summary>
    public int ExitCode { get; set; }

    /// <summary>Gets or sets a test for invocations that crash with exit 139 whatever <see cref="ExitCode"/> says.</summary>
    public Func<FfmpegInvocation, bool> Crashes { get; set; } = _ => false;

    /// <summary>Extracts the quoted output path that ends the argument string.</summary>
    /// <param name="invocation">The invocation.</param>
    /// <returns>The output path.</returns>
    public static string OutputPath(FfmpegInvocation invocation)
    {
        var args = invocation.Arguments.TrimEnd('"');
        return args[(args.LastIndexOf('"') + 1)..];
    }

    /// <inheritdoc/>
    public async Task<FfmpegRunResult> RunAsync(FfmpegInvocation invocation, CancellationToken cancellationToken)
    {
        Invocations.Add(invocation);
        if (Crashes(invocation))
        {
            return new FfmpegRunResult(FfmpegRunStatus.Exited, 139, string.Empty, "Segmentation fault\n", null, TimeSpan.Zero, null);
        }

        if (ExitCode == 0)
        {
            await File.WriteAllTextAsync(OutputPath(invocation), "fake fixture bytes", cancellationToken);
        }

        var stderr = ExitCode == 0 ? string.Empty : "Unknown encoder 'libx264'\n";
        return new FfmpegRunResult(FfmpegRunStatus.Exited, ExitCode, string.Empty, stderr, null, TimeSpan.Zero, null);
    }
}
