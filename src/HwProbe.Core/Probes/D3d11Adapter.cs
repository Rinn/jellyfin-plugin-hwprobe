using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Reads the adapters ffmpeg's D3D11VA device opens use on Windows.</summary>
public static partial class D3d11Adapter
{
    /// <summary>Returns the adapter a device open used.</summary>
    /// <param name="stderr">Verbose stderr of an <c>-init_hw_device d3d11va=dx11:N</c> open.</param>
    /// <returns>The vendor and device IDs as <c>0x</c> and hex, or null when no adapter is named.</returns>
    /// <remarks>
    /// libavutil/hwcontext_d3d11va.c logs "Using device %04x:%04x (%ls)." (lowercase hex) after picking the adapter and
    /// before creating the device, so it appears even when the create fails. An index past the last adapter logs
    /// nothing and silently falls back to the default adapter, which is how the end of the list shows.
    /// </remarks>
    public static (string Vendor, string Device)? Parse(string stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        var match = UsingDevice().Match(stderr);
        return match.Success ? ("0x" + match.Groups[1].Value, "0x" + match.Groups[2].Value) : null;
    }

    /// <summary>Matches the device line.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"Using device ([0-9a-f]+):([0-9a-f]+) \(")]
    private static partial Regex UsingDevice();
}
