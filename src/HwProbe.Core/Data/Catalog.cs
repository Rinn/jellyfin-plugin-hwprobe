using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Speed;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>What the plugin page lists and what a speed run can measure, read from the catalog.yaml compiled into this assembly.</summary>
public sealed partial class Catalog
{
    private static readonly Lazy<Catalog> _default = new(() => Parse(ReadResource()));

    private Dictionary<string, CatalogSetting>? _optionsByKey;

    /// <summary>Gets the catalog compiled into this assembly.</summary>
    public static Catalog Default => _default.Value;

    /// <summary>Gets the video codecs Jellyfin transcodes to, in the page's order.</summary>
    public required IReadOnlyList<CatalogCodec> Codecs { get; init; }

    /// <summary>Gets the qualities a player offers, highest first.</summary>
    public required IReadOnlyList<CatalogQuality> Qualities { get; init; }

    /// <summary>Gets the speed accuracies, in the page's order.</summary>
    public required IReadOnlyList<CatalogMethod> Methods { get; init; }

    /// <summary>Gets the accuracy chosen when the page first loads.</summary>
    public required SpeedMethod DefaultMethod { get; init; }

    /// <summary>Gets the repeat counts offered.</summary>
    public required IReadOnlyList<CatalogOption> Repeats { get; init; }

    /// <summary>Gets the settings a run can be given, in the order of Jellyfin's Transcoding page.</summary>
    public required IReadOnlyList<CatalogSetting> Options { get; init; }

    /// <summary>Gets the time limits per measurement offered, in seconds.</summary>
    public required IReadOnlyList<CatalogOption> TimeLimits { get; init; }

    /// <summary>Gets what a run can do when the server starts transcoding.</summary>
    public required IReadOnlyList<CatalogTranscodeAction> WhenTranscoding { get; init; }

    /// <summary>Gets what a run does when the server starts transcoding and the request doesn't say.</summary>
    public required TranscodeAction DefaultWhenTranscoding { get; init; }

    /// <summary>Gets a value indicating whether a run measures resource usage when the request doesn't say.</summary>
    public bool DefaultMeasureResources { get; init; }

    /// <summary>Gets the seconds between one measurement ending and the next starting, so the system winds down from the last; reused results don't wait.</summary>
    public double TestDelay { get; init; }

    /// <summary>Gets the thresholds measurements are judged by.</summary>
    public required CatalogAdvice Advice { get; init; }

    /// <summary>Gets the accuracy test suites measure at.</summary>
    public required SpeedMethod SuiteMethod { get; init; }

    /// <summary>Gets what the page calls software encoding, the <c>none</c> backend.</summary>
    public required string SoftwareName { get; init; }

    /// <summary>Gets what the page calls each measured resource, keyed as <see cref="Speed.ResourceSaving.Resource"/> is, in sentence case.</summary>
    public required IReadOnlyDictionary<string, string> ResourceNames { get; init; }

    /// <summary>Gets labels for server settings suggestions change that aren't run options, keyed as suggestions name them, with any caveat under <c>{key}Caveat</c>.</summary>
    public required IReadOnlyDictionary<string, string> Labels { get; init; }

    /// <summary>Gets the Resource Usage view's column header for each resource, keyed as <see cref="ResourceNames"/> is.</summary>
    public required IReadOnlyDictionary<string, string> ResourceHeaders { get; init; }

    /// <summary>Gets what each energy meter's domain is called, keyed as <see cref="Resources.ResourceUsage.Joules"/> is.</summary>
    public required IReadOnlyDictionary<string, string> PowerDomains { get; init; }

    /// <summary>Gets the report's remedies, fix links, and findings, by key, with <c>{name}</c> placeholders.</summary>
    public required IReadOnlyDictionary<string, string> Texts { get; init; }

    /// <summary>Gets what inputs' codecs are called, keyed as ffmpeg names them.</summary>
    public required IReadOnlyDictionary<string, string> CodecNames { get; init; }

    /// <summary>Gets the backends, in the order of Jellyfin's dropdown.</summary>
    public required IReadOnlyList<CatalogBackend> Backends { get; init; }

    /// <summary>Gets the test suites, in the page's order.</summary>
    public required IReadOnlyList<CatalogSuite> Suites { get; init; }

    /// <summary>Gets the settings that together pick one thing, each suggested as one table.</summary>
    public IReadOnlyList<CatalogSettingGroup> SettingGroups { get; init; } = [];

    /// <summary>Gets the links the page, the CLI, and the advice point to, by name: <c>repository</c>, <c>issueForm</c>, <c>notices</c>, <c>jellyfinGuides</c>, <c>intelLowPowerGuide</c>, <c>fateSuite</c>.</summary>
    public required IReadOnlyDictionary<string, string> Links { get; init; }

    /// <summary>Gets what the page calls each GPU engine, by the name the platform gives it.</summary>
    public required IReadOnlyDictionary<string, string> GpuEngines { get; init; }

    /// <summary>Gets the pipeline tiers' descriptions.</summary>
    public required IReadOnlyDictionary<PipelineTier, string> Tiers { get; init; }

    /// <summary>Gets the descriptions of backends that don't work.</summary>
    public required IReadOnlyDictionary<BackendVerdict, string> Verdicts { get; init; }

    /// <summary>Gets the descriptions of the report's findings, by code.</summary>
    public required IReadOnlyDictionary<string, string> Findings { get; init; }

    /// <summary>Gets the names of the settings a speed run started from, by <see cref="SpeedSettings"/> property.</summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    /// <summary>Gets the placeholders ffmpeg arguments expand.</summary>
    internal IReadOnlyDictionary<string, string> Vars { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets shared fields the file merges into entries; not read after parsing.</summary>
    internal IReadOnlyDictionary<string, object> Templates { get; init; } = new Dictionary<string, object>();

    /// <summary>Gets the audio track the test videos copy; null only in a file that leaves it out, which <see cref="Check"/> refuses.</summary>
    internal CatalogClip? TestAudio { get; init; }

    /// <summary>Gets the speed videos, in the page's order.</summary>
    internal IReadOnlyList<CatalogVideo> Videos { get; init; } = [];

    /// <summary>Gets the videos chosen when none are asked for.</summary>
    internal IReadOnlyList<string> DefaultVideos { get; init; } = [];

    /// <summary>Gets the decode-only output.</summary>
    internal CatalogOutput? Decode { get; init; }

    /// <summary>Gets the outputs chosen when none are asked for.</summary>
    internal IReadOnlyList<string> DefaultOutputs { get; init; } = [];

    /// <summary>Gets the subtitle files the burn-in variation reads; null only in a file that leaves them out, which <see cref="Check"/> refuses.</summary>
    internal CatalogSubtitles? Subtitles { get; init; }

    /// <summary>Returns a text from <see cref="Texts"/> with its placeholders filled in.</summary>
    /// <param name="key">The text's key.</param>
    /// <param name="values">Each placeholder's name and value.</param>
    /// <returns>The text.</returns>
    public static string Text(string key, params (string Name, string Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var text = Default.Texts.TryGetValue(key, out var found) ? found : throw new InvalidDataException($"catalog.yaml: texts has no {key}.");
        return values.Aggregate(text, (t, v) => t.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));
    }

    /// <summary>Reads and checks a catalog.</summary>
    /// <param name="yaml">The catalog file's text.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="InvalidDataException">The file doesn't parse or is incomplete.</exception>
    public static Catalog Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithNodeTypeResolver(new ReadOnlyTypeResolver())
            .IncludeNonPublicProperties()
            .WithEnforceRequiredMembers()
            .Build();
        Catalog catalog;
        try
        {
            using var reader = new StringReader(yaml);
            catalog = deserializer.Deserialize<Catalog>(new MergingParser(new Parser(reader)));
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException("catalog.yaml: " + ex.Message, ex);
        }

        catalog.Check();
        return catalog;
    }

    /// <summary>Returns the output key for a codec at a quality.</summary>
    /// <param name="codec">The codec.</param>
    /// <param name="quality">The quality.</param>
    /// <returns>e.g. <c>hevc-8mbps</c>.</returns>
    public static string OutputKey(CatalogCodec codec, CatalogQuality quality)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(quality);
        return codec.Key + "-" + quality.Key;
    }

    /// <summary>Returns the setting with a key.</summary>
    /// <param name="key">The setting's key.</param>
    /// <returns>The setting, or null for an unknown key.</returns>
    public CatalogSetting? Option(string key) =>
        (_optionsByKey ??= Options.ToDictionary(o => o.Key, StringComparer.Ordinal)).GetValueOrDefault(key);

    /// <summary>Expands <c>{name}</c> placeholders from <see cref="Vars"/>, including placeholders inside them.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The expanded arguments; unknown placeholders are left.</returns>
    internal string Expand(string arguments)
    {
        for (var depth = 0; depth < 5; depth++)
        {
            var expanded = Placeholder().Replace(arguments, m => Vars.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
            if (expanded == arguments)
            {
                break;
            }

            arguments = expanded;
        }

        return arguments;
    }

    /// <summary>Reads the catalog file compiled into this assembly.</summary>
    /// <returns>Its text.</returns>
    private static string ReadResource()
    {
        using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("catalog.yaml") ?? throw new InvalidDataException("catalog.yaml isn't compiled in.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Requires a set of keys to match every value of an enum but some.</summary>
    /// <typeparam name="T">The enum.</typeparam>
    /// <param name="name">The catalog section, for the message.</param>
    /// <param name="keys">The section's keys.</param>
    /// <param name="except">Values the section leaves out.</param>
    private static void RequireAll<T>(string name, IEnumerable<T> keys, params T[] except)
        where T : struct, Enum
    {
        var listed = keys.ToList();
        var missing = Enum.GetValues<T>().Except(except).Except(listed).ToList();
        if (missing.Count > 0 || listed.Count != listed.Distinct().Count() || listed.Intersect(except).Any())
        {
            throw new InvalidDataException($"catalog.yaml: {name} must list each of {string.Join(", ", Enum.GetValues<T>().Except(except))} once; missing {string.Join(", ", missing)}.");
        }
    }

    /// <summary>Requires keys to be unique, and defaults to be among them.</summary>
    /// <param name="name">The catalog section, for the message.</param>
    /// <param name="keys">The section's keys.</param>
    /// <param name="defaults">The keys chosen by default.</param>
    private static void RequireKeys(string name, IReadOnlyList<string> keys, IReadOnlyList<string> defaults)
    {
        if (keys.Count != keys.Distinct(StringComparer.Ordinal).Count() || defaults.Count == 0 || defaults.Except(keys, StringComparer.Ordinal).Any())
        {
            throw new InvalidDataException($"catalog.yaml: {name} keys must be unique and include the defaults.");
        }
    }

    /// <summary>Matches a <c>{name}</c> placeholder.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    /// <summary>Matches a lowercase SHA-256.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();

    /// <summary>Checks what the file can't say for itself: every enum value is listed, keys are unique, and clips are complete.</summary>
    /// <exception cref="InvalidDataException">Something is missing or wrong.</exception>
    private void Check()
    {
        RequireAll("methods", Methods.Select(m => m.Key));
        RequireAll("whenTranscoding", WhenTranscoding.Select(w => w.Key));
        string[] links = ["repository", "issueForm", "jellyfinGuides", "intelLowPowerGuide", "fateSuite"];
        if (links.Any(l => !Links.TryGetValue(l, out var url) || !Uri.TryCreate(url, UriKind.Absolute, out _)))
        {
            throw new InvalidDataException($"catalog.yaml: links requires an absolute URL for each of {string.Join(", ", links)}.");
        }

        RequireAll("backends", Backends.Select(b => b.Type), HwType.none);
        RequireAll("tiers", Tiers.Keys, PipelineTier.Unknown);
        RequireAll("verdicts", Verdicts.Keys, BackendVerdict.Viable, BackendVerdict.NotBuilt);
        RequireKeys("videos", [.. Videos.Select(v => v.Key), SpeedCatalog.LibraryKey], DefaultVideos);
        if (Decode is null)
        {
            throw new InvalidDataException("catalog.yaml: decode is missing.");
        }

        RequireKeys("outputs", [.. Codecs.SelectMany(c => Qualities.Select(q => OutputKey(c, q))), Decode.Key], DefaultOutputs);
        if (Methods.Any(m => m.Seconds.Count != 2))
        {
            throw new InvalidDataException("catalog.yaml: method seconds are [low, high].");
        }

        if (Options.DistinctBy(o => o.Key, StringComparer.Ordinal).Count() != Options.Count)
        {
            throw new InvalidDataException("catalog.yaml: option keys must be unique.");
        }

        foreach (var option in Options)
        {
            var kinds = (option.Switch ? 1 : 0) + (option.Range is [_, _] ? 1 : 0) + (option.Choices is { Count: > 0 } ? 1 : 0);
            var sample = option.Switch ? "true" : option.Range is [var low, _] ? low.ToString(CultureInfo.InvariantCulture) : option.Choices is [var first, ..] ? first.Key : string.Empty;
            if (kinds != 1 || SpeedSettingsOptions.Apply(new SpeedSettings(), option.Key, sample) is null)
            {
                throw new InvalidDataException($"catalog.yaml: option {option.Key} requires one of switch, range, or choices, and a key SpeedSettingsOptions applies.");
            }

            if ((option.QualityOrder?.Any(v => !option.Takes(v)) ?? false) || (option.LowerIsBetter && option.Range is null))
            {
                throw new InvalidDataException($"catalog.yaml: option {option.Key}'s qualityOrder lists values it doesn't take, or lowerIsBetter is set without a range.");
            }

            if (option.OutputCodec is { } codec && !Codecs.Any(c => c.Key == codec))
            {
                throw new InvalidDataException($"catalog.yaml: option {option.Key}'s outputCodec {codec} isn't a codec the catalog lists.");
            }

            if (option.CompatibleValue is { } compatible && (!option.Takes(compatible) || option.Caveat is null))
            {
                throw new InvalidDataException($"catalog.yaml: option {option.Key}'s compatibleValue requires a caveat and a value it takes.");
            }

            if ((option.Recommended is null) != (option.RecommendedReason is null) || (option.Recommended is { } recommended && !option.Takes(recommended)))
            {
                throw new InvalidDataException($"catalog.yaml: option {option.Key}'s recommended requires a recommendedReason and a value it takes.");
            }
        }

        if (Videos.Select(v => v.Sample).FirstOrDefault(s => s?.HolderUrl is { } site && !Uri.TryCreate(site, UriKind.Absolute, out _)) is { } unlinked)
        {
            throw new InvalidDataException($"catalog.yaml: {unlinked.Title}'s holderUrl requires an absolute URL.");
        }

        CheckAdvice();
        CheckSuites();

        foreach (var group in SettingGroups)
        {
            var known = group.Settings.All(k => Option(k) is not null);
            var conditions = group.Rows.SelectMany(r => r.When ?? new Dictionary<string, string>()).ToList();
            var valid = known
                && conditions.All(c => group.Settings.Contains(c.Key) && Option(c.Key)!.Takes(c.Value))
                && group.Rows.All(r => r.Describes is null || group.Settings.Contains(r.Describes))
                && group.Rows.Select(r => r.Label).Distinct(StringComparer.Ordinal).Count() == group.Rows.Count;
            if (!known || !valid || group.Rows.Count == 0 || SettingGroups.Count(g => g.Settings.Intersect(group.Settings).Any()) > 1)
            {
                throw new InvalidDataException($"catalog.yaml: setting group {group.Key} requires rows with distinct labels, options the catalog has, in no other group, and conditions on its own settings.");
            }
        }

        if (!double.IsFinite(TestDelay) || TestDelay < 0)
        {
            throw new InvalidDataException("catalog.yaml: testDelay requires a number of seconds, zero or more.");
        }

        if (Subtitles is null || TestAudio is null)
        {
            throw new InvalidDataException("catalog.yaml: subtitles or testAudio are missing.");
        }

        if (Videos.FirstOrDefault(v => v.Clip.Download is not null && v.Clip.Size is not > 0) is { } unsized)
        {
            throw new InvalidDataException($"catalog.yaml: {unsized.Key} is downloaded whole and requires its size.");
        }

        foreach (var clip in Videos.Select(v => v.Clip).Append(Subtitles.Text).Append(Subtitles.Image).Append(TestAudio))
        {
            var unknown = Placeholder().Matches(Expand(clip.Arguments)).Select(m => m.Groups[1].Value).Where(n => n != "piece" || clip.Piece is null).ToList();
            var hash = clip.Piece?.Sha256 ?? clip.Sha256;
            var made = clip.Arguments.Length > 0 || clip.Download is not null || clip.Piece is not null;
            if (unknown.Count > 0 || (hash is not null && !Sha256().IsMatch(hash)) || (clip.Download is not null && clip.Sha256 is null) || !made)
            {
                throw new InvalidDataException($"catalog.yaml: {clip.File} requires arguments or a download with a lowercase SHA-256, and no unknown placeholders ({string.Join(", ", unknown)}).");
            }
        }
    }

    /// <summary>Checks each suite names videos, outputs, backends, and settings the catalog has.</summary>
    /// <exception cref="InvalidDataException">A suite names something that isn't there.</exception>
    private void CheckAdvice()
    {
        // YamlDotNet doesn't enforce required members, so a missing block is caught here.
        if (Advice is not { Noise: > 0 and < 1, Headroom: >= 1, MaxStreamLoss: > 0 and < 1, ResourceMargin: > 0 and < 1 })
        {
            throw new InvalidDataException("catalog.yaml: advice requires noise, maxStreamLoss, and resourceMargin between 0 and 1, and headroom of at least 1.");
        }

        string[] resources = ["Cpu", "Memory", "Gpu", "GpuMemory", "Power"];
        if (ResourceNames is null || resources.Any(r => !ResourceNames.ContainsKey(r)) || ResourceHeaders is null || resources.Any(r => !ResourceHeaders.ContainsKey(r)) || Labels is null || !Labels.ContainsKey(Speed.SpeedAdvisor.BitrateLimitKey) || !Labels.ContainsKey("ServerSetting") || !Labels.ContainsKey("ServerSettingAfter") || CodecNames is null || SoftwareName is null || PowerDomains is null)
        {
            throw new InvalidDataException($"catalog.yaml: resourceNames and resourceHeaders require {string.Join(", ", resources)}, labels requires {Speed.SpeedAdvisor.BitrateLimitKey} ServerSetting, and ServerSettingAfter, and codecNames and softwareName are required.");
        }
    }

    /// <summary>Checks each suite names videos, outputs, backends, and settings the catalog has.</summary>
    private void CheckSuites()
    {
        var videos = Videos.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        var outputs = Codecs.SelectMany(c => Qualities.Select(q => OutputKey(c, q))).Append(Decode!.Key).ToHashSet(StringComparer.Ordinal);
        string[] backends = ["configuredAndSoftware", "configured", "software"];
        foreach (var suite in Suites)
        {
            var steps = suite.Steps;
            var wrong =
                !backends.Contains(suite.Backends) ? $"backends {suite.Backends}"
                : suite.Requires is not (null or "lowPower") ? $"requires {suite.Requires}"
                : suite.ThreadSteps == (steps.Count > 0) ? "steps (one of steps or threadSteps)"
                : suite.Videos.Concat(steps.SelectMany(s => s.Videos ?? [])).FirstOrDefault(v => !videos.Contains(v)) is { } video ? $"video {video}"
                : suite.Outputs.Concat(steps.SelectMany(s => s.Outputs ?? [])).FirstOrDefault(o => !outputs.Contains(o)) is { } output ? $"output {output}"
                : steps.FirstOrDefault(s => s.Backends?.Contains(HwType.none) == true) is { } software ? $"backend none on step {software.Label} (steps are for hardware backends)"
                : suite.ThreadSteps && !suite.ThreadLabel.Contains("{n}", StringComparison.Ordinal) ? "threadLabel (it requires {n})"
                : steps.SelectMany(s => s.Options).FirstOrDefault(o => Option(o.Key) is not { } known || !known.Takes(o.Value) || SpeedSettingsOptions.Apply(new SpeedSettings(), o.Key, o.Value) is null) is { Key: not null } option ? $"option {option.Key}={option.Value}"
                : null;
            if (wrong is not null)
            {
                throw new InvalidDataException($"catalog.yaml: suite {suite.Key} has an unknown {wrong}.");
            }
        }
    }
}
