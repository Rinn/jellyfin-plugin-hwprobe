using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Reads the adapter ffmpeg's D3D11VA device opened on Windows.</summary>
public static partial class D3d11Adapter
{
    /// <summary>Microsoft's software adapter (Basic Render Driver), which Windows always lists.</summary>
    public const string SoftwareVendor = "0x1414";

    /// <summary>Returns the PCI vendor ID of the adapter a device open used.</summary>
    /// <param name="stderr">Verbose stderr of an <c>-init_hw_device d3d11va</c> open.</param>
    /// <returns>The vendor as <c>0x</c> and hex, e.g. <c>0x10de</c>, or null when no adapter is named.</returns>
    /// <remarks>
    /// libavutil/hwcontext_d3d11va.c logs "Using device %04x:%04x (%ls)" (lowercase hex) with the DXGI vendor and device IDs, e.g.
    /// <c>Using device 4d4f4351:36334330 (Qualcomm(R) Adreno(TM) X1-85 GPU)</c> on a Snapdragon X Elite.
    /// </remarks>
    public static string? Vendor(string stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        var match = UsingDevice().Match(stderr);
        return match.Success ? "0x" + match.Groups[1].Value : null;
    }

    /// <summary>Matches the device line.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"Using device ([0-9a-f]+):[0-9a-f]+ \(")]
    private static partial Regex UsingDevice();
}
