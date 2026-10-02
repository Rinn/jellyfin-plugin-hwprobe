using System.Text.RegularExpressions;
using Jellyfin.Plugin.HwProbe.Core.Devices;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Reads the adapters ffmpeg's D3D11VA device opens use on Windows.</summary>
public static partial class D3d11Adapter
{
    /// <summary>Microsoft's software adapter (Basic Render Driver), which Windows always lists last.</summary>
    public static readonly (string Vendor, string Device) SoftwareAdapter = ("0x1414", "0x008c");

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

    /// <summary>Returns the GPU vendors among the adapters, or none when the list can't be trusted.</summary>
    /// <param name="adapters">Every adapter, in index order.</param>
    /// <returns>Distinct vendors, sorted; empty when any adapter but the last is Microsoft's, or one is a VM's virtual GPU.</returns>
    /// <remarks>
    /// Windows always lists the software adapter (Basic Render Driver, 1414:008c) last. A GPU without its driver shows as
    /// Microsoft Basic Display Adapter with the same IDs, earlier in the list, and its maker is then unknown.
    /// </remarks>
    public static IReadOnlyList<string> Vendors(IReadOnlyList<(string Vendor, string Device)> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        var real = adapters.Count > 0 && adapters[^1] == SoftwareAdapter ? adapters.Take(adapters.Count - 1).ToList() : [.. adapters];
        return real.Exists(a => VirtualGpus.Vendors.Contains(a.Vendor, StringComparer.Ordinal))
            ? []
            : [.. real.Select(a => a.Vendor).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>Matches the device line.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"Using device ([0-9a-f]+):([0-9a-f]+) \(")]
    private static partial Regex UsingDevice();
}
