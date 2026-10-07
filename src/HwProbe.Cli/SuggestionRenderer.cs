using System.Globalization;
using System.Text;
using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Renders what a suite's runs recommend, one line each, as the page's Suggestions list them.</summary>
internal static class SuggestionRenderer
{
    /// <summary>Renders the suggestions.</summary>
    /// <param name="suggestions">The suggestions.</param>
    /// <returns>The text, newline-terminated, or empty when there are none.</returns>
    public static string Render(IReadOnlyList<SpeedSuggestion> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        if (suggestions.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder("\nrecommend\n");
        foreach (var s in suggestions)
        {
            var label = s.Setting is null ? null : Catalog.Default.Options.FirstOrDefault(o => o.Key == s.Setting)?.Label ?? Catalog.Default.Labels.GetValueOrDefault(s.Setting) ?? s.Setting;
            var current = s.Current ? $" ({Catalog.Default.Labels["ServerSettingAfter"]})" : string.Empty;
            var others = OrList([.. s.Others.Select(o => Value(s.Setting, o))]);
            var value = Value(s.Setting, s.Value);
            var savings = string.Join(", ", s.Savings.Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Fraction:0%} less {Catalog.Default.ResourceNames.GetValueOrDefault(x.Resource, x.Resource)}")));
            var line = s.Kind switch
            {
                SpeedSuggestionKind.FastestBackend => $"hardware acceleration: {SpeedRenderer.Name(s.Type!.Value)}{(s.Savings.Count > 0 ? $", as fast with {savings}" : string.Empty)}",
                SpeedSuggestionKind.FallsBehind => $"{SpeedRenderer.Name(s.Type!.Value)} falls behind real time",
                SpeedSuggestionKind.TooSlowEverywhere => string.Create(CultureInfo.InvariantCulture, $"too slow on every backend: {s.Outputs[0]}, fastest {SpeedRenderer.Name(s.Type!.Value)} at {s.Speed:0.00}x real time"),
                SpeedSuggestionKind.FasterSetting => string.Create(CultureInfo.InvariantCulture, $"{label}: {value}{current}, {Math.Abs(s.Gain ?? 0):0%} faster than {others}{(s.LowerQuality ? ", at lower quality" : string.Empty)}"),
                SpeedSuggestionKind.EfficientSetting => $"{label}: {value}{current}, as fast as {others} with {savings}{(s.LowerQuality ? ", at lower quality" : string.Empty)}",
                SpeedSuggestionKind.HigherQuality => string.Create(CultureInfo.InvariantCulture, $"{label}: {value}{current}, better quality than {others}, still {s.Speed:0.0}x real time"),
                SpeedSuggestionKind.Compatible when s.Setting == SpeedAdvisor.BitrateLimitKey => string.Create(CultureInfo.InvariantCulture, $"{label}: no limit, the highest quality, slowest {s.Speed:0.0}x real time"),
                SpeedSuggestionKind.Compatible => string.Create(CultureInfo.InvariantCulture, $"{label}: {value}{current}, more compatible than {others}, at lower quality, {s.Speed:0.0}x real time"),
                SpeedSuggestionKind.BitrateLimit => string.Create(CultureInfo.InvariantCulture, $"{label}: {int.Parse(s.Value!, CultureInfo.InvariantCulture) / 1e6:0.#} Mbps, the highest H.264 quality that keeps real time"),
                SpeedSuggestionKind.NoChange => $"{label}: {value}{current}, nothing compared is worth changing to ({others})",
                SpeedSuggestionKind.RecommendedValue => $"{label}: {value}{current}, {Catalog.Default.Options.First(o => o.Key == s.Setting).RecommendedReason!.TrimEnd('.')}",
                _ => s.Kind.ToString(),
            };
            var streams = s.Streams is { } mine && s.OtherStreams is { } theirs && mine != theirs ? string.Create(CultureInfo.InvariantCulture, $"; streams {theirs}{(s.OtherStreamsCapped ? "+" : string.Empty)} -> {mine}{(s.StreamsCapped ? "+" : string.Empty)}") : string.Empty;
            text.Append("  ").Append(line).Append(streams).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Joins values as alternatives, with a serial comma: <c>a</c>, <c>a or b</c>, <c>a, b, or c</c>.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The list.</returns>
    private static string OrList(IReadOnlyList<string> values) =>
        values.Count > 2 ? string.Join(", ", values.Take(values.Count - 1)) + ", or " + values[^1] : string.Join(" or ", values);

    /// <summary>Names a setting's value as the page does: a choice's label, or On and Off for a switch.</summary>
    /// <param name="setting">The catalog option key, or null.</param>
    /// <param name="value">The value as the catalog keys it.</param>
    /// <returns>The name.</returns>
    private static string Value(string? setting, string? value)
    {
        var option = Catalog.Default.Options.FirstOrDefault(o => o.Key == setting);
        return option?.Choices?.FirstOrDefault(c => c.Key == value)?.Label ?? (option?.Switch == true ? (value == "true" ? "On" : "Off") : value ?? string.Empty);
    }
}
