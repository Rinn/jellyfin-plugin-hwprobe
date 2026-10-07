using System.Globalization;
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

    // Performance tests in HwProbe 1.1.0 to 1.1.2 could apply the thread count, so those history entries still revert.
    private static readonly Dictionary<string, (Func<TrickplayOptions, int> Get, Action<TrickplayOptions, int> Set)> _numbers = new(StringComparer.Ordinal)
    {
        [Prefix + nameof(TrickplayOptions.ProcessThreads)] = (o => o.ProcessThreads, (o, v) => o.ProcessThreads = v),
    };

    /// <summary>Reports whether a key names a trickplay value HwProbe may change.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns>True for a known key.</returns>
    public static bool IsKnown(string key) => _flags.ContainsKey(key) || _numbers.ContainsKey(key);

    /// <summary>Reads a value.</summary>
    /// <param name="options">The options.</param>
    /// <param name="key">A known setting key.</param>
    /// <returns><c>true</c> or <c>false</c>, or a whole number.</returns>
    public static string Read(TrickplayOptions options, string key)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _flags.TryGetValue(key, out var flag) ? EncodingSettings.Format(flag.Get(options))
            : _numbers.TryGetValue(key, out var number) ? number.Get(options).ToString(CultureInfo.InvariantCulture)
            : throw new ArgumentException($"Unknown setting {key}.", nameof(key));
    }

    /// <summary>Writes a value.</summary>
    /// <param name="options">The options to change.</param>
    /// <param name="key">A known setting key.</param>
    /// <param name="value"><c>true</c> or <c>false</c>, or a whole number.</param>
    public static void Write(TrickplayOptions options, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(value);
        if (_flags.TryGetValue(key, out var flag))
        {
            flag.Set(options, bool.Parse(value));
        }
        else if (_numbers.TryGetValue(key, out var number))
        {
            number.Set(options, int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture));
        }
        else
        {
            throw new ArgumentException($"Unknown setting {key}.", nameof(key));
        }
    }
}
