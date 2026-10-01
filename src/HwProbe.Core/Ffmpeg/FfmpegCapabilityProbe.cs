namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Enumerates what an ffmpeg binary was built with. No device access.</summary>
public sealed class FfmpegCapabilityProbe
{
    private static readonly Dictionary<string, string?> _noEnvironment = [];

    private readonly IFfmpegRunner _runner;
    private readonly TimeSpan _timeout;

    /// <summary>Initializes a new instance of the <see cref="FfmpegCapabilityProbe"/> class.</summary>
    /// <param name="runner">Launches ffmpeg.</param>
    /// <param name="timeout">Hard timeout per enumeration launch.</param>
    public FfmpegCapabilityProbe(IFfmpegRunner runner, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _timeout = timeout;
    }

    /// <summary>Enumerates the binary's build capabilities.</summary>
    /// <param name="ffmpegPath">The binary to enumerate.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>The capabilities; check <see cref="FfmpegCapabilities.Validation"/> before trusting them.</returns>
    public async Task<FfmpegCapabilities> ProbeAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(ffmpegPath);

        // Arguments match EncoderValidator exactly; each listing is read from stdout.
        var versionOutput = await StdoutAsync(ffmpegPath, "-version", cancellationToken);
        var hwaccels = CapabilityParser.ParseHwaccels(await StdoutAsync(ffmpegPath, "-hwaccels", cancellationToken));
        var encoders = CapabilityParser.ParseCodecs(await StdoutAsync(ffmpegPath, "-encoders", cancellationToken));
        var decoders = CapabilityParser.ParseCodecs(await StdoutAsync(ffmpegPath, "-decoders", cancellationToken));
        var filters = CapabilityParser.ParseFilters(await StdoutAsync(ffmpegPath, "-filters", cancellationToken));

        var filterOptions = new Dictionary<string, bool>(StringComparer.Ordinal);
        var helpByFilter = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var check in FilterOptionCheck.All)
        {
            // Skip the launch for filters absent from the build; the check can only fail.
            if (!filters.Contains(check.Filter))
            {
                filterOptions[check.Key] = false;
                continue;
            }

            if (!helpByFilter.TryGetValue(check.Filter, out var help))
            {
                help = await StdoutAsync(ffmpegPath, "-h filter=" + check.Filter, cancellationToken);
                helpByFilter[check.Filter] = help;
            }

            filterOptions[check.Key] = CapabilityParser.HasFilterOption(help, check.Filter, check.RequiredText);
        }

        return new FfmpegCapabilities(
            ffmpegPath,
            versionOutput,
            FfmpegVersionParser.Parse(versionOutput),
            FfmpegVersionParser.Validate(versionOutput),
            FfmpegVersionParser.IsJellyfinBuild(versionOutput),
            hwaccels,
            encoders,
            decoders,
            filters,
            filterOptions,
            BuildStatusResolver.Resolve(hwaccels, encoders));
    }

    /// <summary>Runs one enumeration and returns its stdout, or empty if it did not exit cleanly.</summary>
    /// <param name="ffmpegPath">The binary.</param>
    /// <param name="arguments">The enumeration arguments.</param>
    /// <param name="cancellationToken">Cancels the launch.</param>
    /// <returns>The stdout, or empty on launch failure or timeout.</returns>
    private async Task<string> StdoutAsync(string ffmpegPath, string arguments, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(new FfmpegInvocation(ffmpegPath, arguments, _noEnvironment, _timeout), cancellationToken);

        // Upstream treats a failed launch as empty output (EncoderValidator.GetProcessOutput callers).
        return result.Status == FfmpegRunStatus.Exited ? result.Stdout : string.Empty;
    }
}
