using MediaBrowser.Model.Configuration;

namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>Copies of the server's encoding and trickplay options, read and written by setting key.</summary>
/// <param name="Encoding">The encoding options.</param>
/// <param name="Trickplay">The trickplay options.</param>
public sealed record ServerSettings(EncodingOptions Encoding, TrickplayOptions Trickplay)
{
    /// <summary>Reports whether a key names a value HwProbe may change.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns>True for a known key.</returns>
    public static bool IsKnown(string key) => TrickplaySettings.IsKnown(key) || EncodingSettings.IsKnown(key);

    /// <summary>Reports whether a key names a trickplay option.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns>True for a trickplay key.</returns>
    public static bool IsTrickplay(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.StartsWith(TrickplaySettings.Prefix, StringComparison.Ordinal);
    }

    /// <summary>Reads a value.</summary>
    /// <param name="key">A known setting key.</param>
    /// <returns>The value in the form history entries store it.</returns>
    public string Read(string key) => IsTrickplay(key) ? TrickplaySettings.Read(Trickplay, key) : EncodingSettings.Read(Encoding, key);

    /// <summary>Writes a value.</summary>
    /// <param name="key">A known setting key.</param>
    /// <param name="value">A value in the form <see cref="Read"/> returns.</param>
    public void Write(string key, string value)
    {
        if (IsTrickplay(key))
        {
            TrickplaySettings.Write(Trickplay, key, value);
        }
        else
        {
            EncodingSettings.Write(Encoding, key, value);
        }
    }
}
