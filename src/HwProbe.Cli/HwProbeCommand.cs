using System.CommandLine;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Defines the hwprobe command line and binds it to <see cref="CliOptions"/>.</summary>
internal sealed class HwProbeCommand
{
    private readonly Option<string?> _ffmpeg = new("--ffmpeg") { Description = "ffmpeg binary. Default: auto-discover." };
    private readonly Option<StopStage> _stage = new("--stage") { Description = "Stop after this step: build, devices or matrix.", DefaultValueFactory = _ => StopStage.Matrix };
    private readonly Option<IReadOnlySet<HwType>> _types = new("--type")
    {
        Description = "Restrict to backends, e.g. vaapi,qsv.",
        CustomParser = ParseTypes,
        Arity = ArgumentArity.OneOrMore,
        DefaultValueFactory = _ => new HashSet<HwType>(),
    };

    private readonly Option<string?> _device = new("--device") { Description = "Restrict to one device node or adapter index." };
    private readonly Option<OutputFormat> _format = new("--format") { Description = "Stdout format.", DefaultValueFactory = _ => OutputFormat.Table };
    private readonly Option<string?> _json = new("--json") { Description = "Also write the JSON report to this file." };
    private readonly Option<string?> _diagnostics = new("--diagnostics") { Description = "Also write a zip of the report and every ffmpeg log, to attach to an issue. Runs a fresh probe." };
    private readonly Option<SpeedMethod?> _speed = new("--speed")
    {
        Description = "After the probe, measure the speed of each working backend and software: quick (speed only), confirm or full (also concurrent streams, starting from the speed or from one).",
        Arity = ArgumentArity.ZeroOrOne,
        CustomParser = r => r.Tokens.Count == 0 ? SpeedMethod.Confirm : Enum.TryParse<SpeedMethod>(r.Tokens[0].Value, ignoreCase: true, out var m) ? m : Error<SpeedMethod?>(r, $"Unknown speed method '{r.Tokens[0].Value}'. Expected: quick, confirm, full."),
    };

    private readonly Option<IReadOnlyList<string>> _speedVideos = new("--speed-videos")
    {
        Description = $"Videos to measure, comma-separated. Default: {string.Join(',', SpeedCatalog.DefaultVideos)}. All: {string.Join(',', SpeedCatalog.Videos.Select(v => v.Key))}, and library with --speed-file.",
        CustomParser = r => ParseKeys(r, k => SpeedCatalog.FindVideo(k) is not null || k == SpeedCatalog.LibraryKey, "video", SpeedCatalog.Videos.Select(v => v.Key)),
        DefaultValueFactory = _ => SpeedCatalog.DefaultVideos,
    };

    private readonly Option<IReadOnlyList<string>> _speedOutputs = new("--speed-outputs")
    {
        Description = $"Outputs to make from every video, comma-separated. Default: {string.Join(',', SpeedCatalog.DefaultOutputs)}. All: {string.Join(',', SpeedCatalog.Outputs.Select(o => o.Key))}.",
        CustomParser = r => ParseKeys(r, k => SpeedCatalog.FindOutput(k) is not null, "output", SpeedCatalog.Outputs.Select(o => o.Key)),
        DefaultValueFactory = _ => SpeedCatalog.DefaultOutputs,
    };

    private readonly Option<SpeedComparison> _speedCompare = new("--speed-compare")
    {
        Description = "Also measure with one setting changed: vbr, preset, quality, bitrate, deinterlace, paths, subtitles; comma-separated.",
        CustomParser = ParseComparisons,
    };

    private readonly Option<string?> _speedFile = new("--speed-file") { Description = "A video file to measure with --speed, as the library video." };
    private readonly Option<int> _speedRepeats = new("--speed-repeats") { Description = "Run each speed measurement 1 to 3 times and report the median.", DefaultValueFactory = _ => 1 };
    private readonly Option<int?> _speedTimeLimit = new("--speed-time-limit") { Description = "Seconds each speed measurement may take before it reports what it has." };
    private readonly Option<string?> _speedJson = new("--speed-json") { Description = "Also write the speed report to this file." };
    private readonly Option<int> _timeout = new("--timeout") { Description = "Per-probe hard timeout, seconds.", DefaultValueFactory = _ => 15 };
    private readonly Option<int> _fixtureTimeout = new("--fixture-timeout") { Description = "Fixture generation timeout, seconds.", DefaultValueFactory = _ => 120 };
    private readonly Option<bool> _refresh = new("--refresh") { Description = "Ignore cached results for this fingerprint." };
    private readonly Option<string> _fixtures = new("--fixtures")
    {
        Description = "Fixture cache directory.",
        DefaultValueFactory = _ => Path.Combine(CacheDirectory.Resolve(), "fixtures"),
    };

    private readonly Option<bool> _expectHw = new("--expect-hw") { Description = "Exit 1 if no backend is viable." };
    private readonly Option<bool> _verbose = new("--verbose") { Description = "Echo every ffmpeg command line and stderr tail." };

    /// <summary>Initializes a new instance of the <see cref="HwProbeCommand"/> class.</summary>
    public HwProbeCommand()
    {
        _timeout.Validators.Add(r => RequirePositive(r, "--timeout"));
        _fixtureTimeout.Validators.Add(r => RequirePositive(r, "--fixture-timeout"));
        _speedRepeats.Validators.Add(r =>
        {
            if (r.GetValueOrDefault<int>() is < 1 or > 3)
            {
                r.AddError("--speed-repeats must be 1, 2 or 3.");
            }
        });
        _speedTimeLimit.Validators.Add(r =>
        {
            if (r.GetValueOrDefault<int?>() is <= 0)
            {
                r.AddError("--speed-time-limit must be a positive number of seconds.");
            }
        });

        Root = new RootCommand("Device-verified hardware transcode detection for Jellyfin.")
        {
            _ffmpeg, _stage, _types, _device, _format, _json, _diagnostics, _speed, _speedVideos, _speedOutputs, _speedCompare, _speedFile, _speedRepeats, _speedTimeLimit, _speedJson, _timeout, _fixtureTimeout, _refresh, _fixtures, _expectHw, _verbose,
        };
    }

    /// <summary>Gets the root command.</summary>
    public RootCommand Root { get; }

    /// <summary>Binds a successful parse to options.</summary>
    /// <param name="result">A parse result with no errors.</param>
    /// <returns>The bound options.</returns>
    public CliOptions Bind(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new CliOptions(
            result.GetValue(_ffmpeg),
            result.GetValue(_stage),
            result.GetValue(_types)!,
            result.GetValue(_device),
            result.GetValue(_format),
            result.GetValue(_json),
            TimeSpan.FromSeconds(result.GetValue(_timeout)),
            TimeSpan.FromSeconds(result.GetValue(_fixtureTimeout)),
            result.GetValue(_refresh),
            Path.GetFullPath(result.GetValue(_fixtures)!),
            result.GetValue(_expectHw),
            result.GetValue(_verbose))
        {
            DiagnosticsPath = result.GetValue(_diagnostics),
            Speed = result.GetValue(_speed) is { } method ? SpeedFrom(result, method) : null,
            SpeedJsonPath = result.GetValue(_speedJson),
            SpeedFilePath = result.GetValue(_speedFile) is { } file ? Path.GetFullPath(file) : null,
        };
    }

    /// <summary>Parses a comma-separated backend list.</summary>
    /// <param name="result">The option's argument result.</param>
    /// <returns>The backends, or an empty set after reporting an error.</returns>
    private static HashSet<HwType> ParseTypes(System.CommandLine.Parsing.ArgumentResult result)
    {
        var types = new HashSet<HwType>();
        foreach (var token in result.Tokens)
        {
            foreach (var name in token.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<HwType>(name, ignoreCase: true, out var type) && type != HwType.none && Enum.IsDefined(type))
                {
                    types.Add(type);
                }
                else
                {
                    result.AddError($"Unknown backend '{name}'. Expected: amf, qsv, nvenc, v4l2m2m, vaapi, videotoolbox, rkmpp.");
                }
            }
        }

        return types;
    }

    /// <summary>Parses a comma-separated list of video or output keys.</summary>
    /// <param name="result">The option's argument result.</param>
    /// <param name="known">Whether a key exists.</param>
    /// <param name="kind">What the keys name, for the error.</param>
    /// <param name="all">Every key, for the error.</param>
    /// <returns>The keys, after reporting any unknown one.</returns>
    private static List<string> ParseKeys(System.CommandLine.Parsing.ArgumentResult result, Func<string, bool> known, string kind, IEnumerable<string> all)
    {
        var keys = result.Tokens.SelectMany(t => t.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        foreach (var unknown in keys.Where(k => !known(k)))
        {
            result.AddError($"Unknown speed {kind} '{unknown}'. Expected: {string.Join(", ", all)}.");
        }

        return keys;
    }

    /// <summary>Parses a comma-separated comparison list.</summary>
    /// <param name="result">The option's argument result.</param>
    /// <returns>The comparisons, after reporting any unknown one.</returns>
    private static SpeedComparison ParseComparisons(System.CommandLine.Parsing.ArgumentResult result)
    {
        var comparisons = SpeedComparison.None;
        foreach (var name in result.Tokens.SelectMany(t => t.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            comparisons |= name.ToUpperInvariant() switch
            {
                "VBR" => SpeedComparison.AudioVbr,
                "PRESET" => SpeedComparison.Preset,
                "QUALITY" => SpeedComparison.Quality,
                "BITRATE" => SpeedComparison.Bitrate,
                "DEINTERLACE" => SpeedComparison.Deinterlace,
                "PATHS" => SpeedComparison.Paths,
                "SUBTITLES" => SpeedComparison.Subtitles,
                _ => Error<SpeedComparison>(result, $"Unknown comparison '{name}'. Expected: vbr, preset, quality, bitrate, deinterlace, paths, subtitles."),
            };
        }

        return comparisons;
    }

    /// <summary>Reports a parse error and returns a placeholder value.</summary>
    /// <typeparam name="T">The option's value type.</typeparam>
    /// <param name="result">The option's argument result.</param>
    /// <param name="message">The error.</param>
    /// <returns>The default value.</returns>
    private static T Error<T>(System.CommandLine.Parsing.ArgumentResult result, string message)
    {
        result.AddError(message);
        return default!;
    }

    /// <summary>Rejects zero or negative second counts.</summary>
    /// <param name="result">The option result to validate.</param>
    /// <param name="name">Option name for the error message.</param>
    private static void RequirePositive(System.CommandLine.Parsing.OptionResult result, string name)
    {
        if (result.GetValueOrDefault<int>() <= 0)
        {
            result.AddError($"{name} must be a positive number of seconds.");
        }
    }

    /// <summary>Binds the speed options.</summary>
    /// <param name="result">The parse result.</param>
    /// <param name="method">The chosen method.</param>
    /// <returns>The options.</returns>
    private SpeedOptions SpeedFrom(ParseResult result, SpeedMethod method) =>
        new(method, result.GetValue(_speedVideos)!, result.GetValue(_speedOutputs)!, result.GetValue(_speedCompare), new SpeedSettings())
        {
            Repeats = result.GetValue(_speedRepeats),
            TimeLimit = result.GetValue(_speedTimeLimit) is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
        };
}
