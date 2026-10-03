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

    /// <summary>Gets a line shown under it, or null.</summary>
    public string? Help { get; init; }

    /// <summary>Gets a value indicating whether it's on or off: <c>true</c> or <c>false</c>.</summary>
    public bool Switch { get; init; }

    /// <summary>Gets the lowest and highest whole number it takes, or null.</summary>
    public IReadOnlyList<int>? Range { get; init; }

    /// <summary>Gets its keyed values, or null.</summary>
    public IReadOnlyList<CatalogLabel>? Choices { get; init; }

    /// <summary>Returns whether a value is one it takes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Whether it's valid.</returns>
    public bool Takes(string value) =>
        Switch ? value is "true" or "false"
        : Range is [var low, var high] ? int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n >= low && n <= high
        : Choices?.Any(c => c.Key == value) == true;
}
