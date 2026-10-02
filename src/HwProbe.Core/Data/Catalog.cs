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

    /// <summary>Gets the catalog compiled into this assembly.</summary>
    public static Catalog Default => _default.Value;

    /// <summary>Gets the video codecs Jellyfin transcodes to, in the page's order.</summary>
    public required IReadOnlyList<CatalogCodec> Codecs { get; init; }

    /// <summary>Gets the qualities a player offers, highest first.</summary>
    public required IReadOnlyList<CatalogQuality> Qualities { get; init; }

    /// <summary>Gets the speed variations, in the page's order.</summary>
    public required IReadOnlyList<CatalogVariation> Variations { get; init; }

    /// <summary>Gets the speed accuracies, in the page's order.</summary>
    public required IReadOnlyList<CatalogMethod> Methods { get; init; }

    /// <summary>Gets the accuracy chosen when the page first loads.</summary>
    public required SpeedMethod DefaultMethod { get; init; }

    /// <summary>Gets the repeat counts offered.</summary>
    public required IReadOnlyList<CatalogOption> Repeats { get; init; }

    /// <summary>Gets Jellyfin's transcoding thread counts.</summary>
    public required IReadOnlyList<CatalogOption> Threads { get; init; }

    /// <summary>Gets the time limits per measurement offered, in seconds.</summary>
    public required IReadOnlyList<CatalogOption> TimeLimits { get; init; }

    /// <summary>Gets the backends, in the order of Jellyfin's dropdown.</summary>
    public required IReadOnlyList<CatalogBackend> Backends { get; init; }

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
        RequireAll("variations", Variations.Select(v => Enum.TryParse<SpeedComparison>(v.Key, out var key) && Enum.IsDefined(key) ? key : SpeedComparison.None), SpeedComparison.None);
        RequireAll("methods", Methods.Select(m => m.Key));
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

        if (Subtitles is null)
        {
            throw new InvalidDataException("catalog.yaml: subtitles are missing.");
        }

        foreach (var clip in Videos.Select(v => v.Clip).Append(Subtitles.Text).Append(Subtitles.Image))
        {
            var unknown = Placeholder().Matches(Expand(clip.Arguments)).Select(m => m.Groups[1].Value).Where(n => n != "piece" || clip.Piece is null).ToList();
            var hash = clip.Piece?.Sha256 ?? clip.Sha256;
            var made = clip.Arguments.Length > 0 || clip.Download is not null;
            if (unknown.Count > 0 || (hash is not null && !Sha256().IsMatch(hash)) || (clip.Download is not null && clip.Sha256 is null) || !made)
            {
                throw new InvalidDataException($"catalog.yaml: {clip.File} needs arguments or a download with a lowercase SHA-256, and no unknown placeholders ({string.Join(", ", unknown)}).");
            }
        }
    }
}
