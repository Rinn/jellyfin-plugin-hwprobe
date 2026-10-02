using MediaBrowser.Model.Configuration;

namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>Reads and writes the <see cref="TrickplayOptions"/> values HwProbe may change, by setting key.</summary>
public static class TrickplaySettings
{
    /// <summary>Prefix of a key naming a trickplay option.</summary>
    public const string Prefix = "Trickplay:";

    private static readonly Dictionary<string, (Func<TrickplayOptions, bool> Get, Action<TrickplayOptions, bool> Set)> _flags = new(StringComparer.Ordinal)
    {
        [Prefix + nameof(TrickplayOptions.EnableHwAcceleration)] = (o => o.EnableHwAcceleration, (o, v) => o.EnableHwAcceleration = v),
        [Prefix + nameof(TrickplayOptions.EnableHwEncoding)] = (o => o.EnableHwEncoding, (o, v) => o.EnableHwEncoding = v),
        [Prefix + nameof(TrickplayOptions.EnableKeyFrameOnlyExtraction)] = (o => o.EnableKeyFrameOnlyExtraction, (o, v) => o.EnableKeyFrameOnlyExtraction = v),
    };

    /// <summary>Reports whether a key names a trickplay value HwProbe may change.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns>True for a known key.</returns>
    public static bool IsKnown(string key) => _flags.ContainsKey(key);

    /// <summary>Reads a value.</summary>
    /// <param name="options">The options.</param>
    /// <param name="key">A known setting key.</param>
    /// <returns><c>true</c> or <c>false</c>.</returns>
    public static string Read(TrickplayOptions options, string key)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _flags.TryGetValue(key, out var flag) ? EncodingSettings.Format(flag.Get(options)) : throw new ArgumentException($"Unknown setting {key}.", nameof(key));
    }

    /// <summary>Writes a value.</summary>
    /// <param name="options">The options to change.</param>
    /// <param name="key">A known setting key.</param>
    /// <param name="value"><c>true</c> or <c>false</c>.</param>
    public static void Write(TrickplayOptions options, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(value);
        if (!_flags.TryGetValue(key, out var flag))
        {
            throw new ArgumentException($"Unknown setting {key}.", nameof(key));
        }

        flag.Set(options, bool.Parse(value));
    }
}
