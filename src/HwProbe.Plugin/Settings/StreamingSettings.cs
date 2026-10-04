using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>Reads and writes the <see cref="StreamingOptions"/> values HwProbe may change, by setting key.</summary>
public static class StreamingSettings
{
    /// <summary>Prefix of a key naming a streaming value.</summary>
    public const string Prefix = "Streaming:";

    /// <summary>The key of the Internet streaming bitrate limit.</summary>
    public const string RemoteClientBitrateLimit = Prefix + nameof(StreamingOptions.RemoteClientBitrateLimit);

    /// <summary>Reports whether a key names a streaming value HwProbe may change.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns>True for a known key.</returns>
    public static bool IsKnown(string key) => key == RemoteClientBitrateLimit;

    /// <summary>Reads a value.</summary>
    /// <param name="options">The options.</param>
    /// <param name="key">A known setting key.</param>
    /// <returns>The value in bits per second.</returns>
    public static string Read(StreamingOptions options, string key)
    {
        ArgumentNullException.ThrowIfNull(options);
        return IsKnown(key) ? options.RemoteClientBitrateLimit.ToString(CultureInfo.InvariantCulture) : throw new ArgumentException($"Unknown setting {key}.", nameof(key));
    }

    /// <summary>Writes a value.</summary>
    /// <param name="options">The options to change.</param>
    /// <param name="key">A known setting key.</param>
    /// <param name="value">The value in bits per second.</param>
    public static void Write(StreamingOptions options, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!IsKnown(key))
        {
            throw new ArgumentException($"Unknown setting {key}.", nameof(key));
        }

        options.RemoteClientBitrateLimit = int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
