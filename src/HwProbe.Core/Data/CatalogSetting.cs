using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A setting a speed run can be given, shown as a select defaulting to the server's own.</summary>
public sealed class CatalogSetting
{
    /// <summary>Gets the key, applied by <see cref="Speed.SpeedSettingsOptions"/>.</summary>
    public required string Key { get; init; }

    /// <summary>Gets jellyfin-web's label for it.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the <c>EncodingOptions</c> property it defaults to, or null when the first choice is the default.</summary>
    public string? Server { get; init; }

    /// <summary>Gets the backend it applies to alone, shown with the backends when that one works, or null for every backend.</summary>
    public string? Backend { get; init; }

    /// <summary>Gets the report setting that says whether the backend supports it, or null.</summary>
    public string? Report { get; init; }

    /// <summary>Gets a line shown under it, or null.</summary>
    public string? Help { get; init; }

    /// <summary>Gets lines shown under it per backend, keyed by backend type with <c>none</c> for software; the page lists the backends that work.</summary>
    public IReadOnlyDictionary<Model.HwType, string>? BackendNotes { get; init; }

    /// <summary>Gets a value indicating whether it's on or off: <c>true</c> or <c>false</c>.</summary>
    public bool Switch { get; init; }

    /// <summary>Gets the lowest and highest whole number it takes, or null.</summary>
    public IReadOnlyList<int>? Range { get; init; }

    /// <summary>Gets its keyed values, or null.</summary>
    public IReadOnlyList<CatalogLabel>? Choices { get; init; }

    /// <summary>Gets its values from best picture to fastest, for a choice or switch that trades speed for quality, or null.</summary>
    public IReadOnlyList<string>? QualityOrder { get; init; }

    /// <summary>Gets a known drawback, shown with any suggestion to change it, or null.</summary>
    public string? Caveat { get; init; }

    /// <summary>Gets a value indicating whether a lower number in its range gives a better picture.</summary>
    public bool LowerIsBetter { get; init; }

    /// <summary>Returns whether one value gives a better picture than another.</summary>
    /// <param name="value">The value.</param>
    /// <param name="other">The other value.</param>
    /// <returns>True when the setting trades speed for quality and the value is the better-quality one.</returns>
    public bool IsBetterQuality(string value, string other)
    {
        if (LowerIsBetter)
        {
            return Takes(value) && Takes(other) && int.Parse(value, CultureInfo.InvariantCulture) < int.Parse(other, CultureInfo.InvariantCulture);
        }

        var order = QualityOrder?.ToList() ?? [];
        return order.IndexOf(value) is var mine and >= 0 && order.IndexOf(other) is var theirs and >= 0 && mine < theirs;
    }

    /// <summary>Returns whether a value is one it takes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Whether it's valid.</returns>
    public bool Takes(string value) =>
        Switch ? value is "true" or "false"
        : Range is [var low, var high] ? int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n >= low && n <= high
        : Choices?.Any(c => c.Key == value) == true;
}
