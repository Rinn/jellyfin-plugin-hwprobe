using System.CommandLine;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;

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

        Root = new RootCommand("Device-verified hardware transcode detection for Jellyfin.")
        {
            _ffmpeg, _stage, _types, _device, _format, _json, _timeout, _fixtureTimeout, _refresh, _fixtures, _expectHw, _verbose,
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
            result.GetValue(_verbose));
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
}
