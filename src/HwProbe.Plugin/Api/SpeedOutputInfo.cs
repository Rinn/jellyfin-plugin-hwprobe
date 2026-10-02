using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>One output, as the page lists it.</summary>
/// <param name="Key">The output key.</param>
/// <param name="Label">What it makes.</param>
/// <param name="DecodeOnly">Whether it only decodes.</param>
/// <param name="Default">Whether it's chosen when the page first loads.</param>
public sealed record SpeedOutputInfo(string Key, string Label, bool DecodeOnly, bool Default)
{
    /// <summary>Returns the page's view of an output.</summary>
    /// <param name="output">The output.</param>
    /// <returns>The view.</returns>
    public static SpeedOutputInfo From(SpeedOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new(output.Key, output.Label, output.Codec is null, SpeedCatalog.DefaultOutputs.Contains(output.Key));
    }
}
