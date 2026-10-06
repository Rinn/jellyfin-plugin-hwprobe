using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Probe cells and a checked build shared by the <see cref="ArgumentSource"/> tests.</summary>
internal static class ArgumentSourceCells
{
    /// <summary>The render node passed to device-backed backends.</summary>
    public const string Node = "/dev/dri/renderD128";

    /// <summary>Gets the smoke probe cell: 8-bit h264 to h264 at 320x240.</summary>
    public static ProbeCell Smoke { get; } = new("h264", 8, "h264", HardwareDecode: true, HardwareEncode: true)
    {
        MaxWidth = 320,
        MaxHeight = 240,
    };

    /// <summary>Gets an HDR10 hevc cell that tone-maps to h264.</summary>
    public static ProbeCell Hdr10 { get; } = new("hevc", 10, "h264", HardwareDecode: true, HardwareEncode: true)
    {
        Profile = "Main 10",
        ColorTransfer = "smpte2084",
        ColorPrimaries = "bt2020",
        ColorSpace = "bt2020nc",
        Tonemap = true,
    };

    /// <summary>Builds arguments with the full capability set, asserting no unmodelled member was reached.</summary>
    /// <param name="recorder">Records the members generation reaches.</param>
    /// <param name="type">The backend.</param>
    /// <param name="device">The device, or null.</param>
    /// <param name="cell">The media shape.</param>
    /// <returns>The generated arguments.</returns>
    public static ProbeArguments Build(CallRecorder recorder, HwType type, string? device, ProbeCell cell)
    {
        var args = new ArgumentSource(TestCapabilities.Full, recorder).Build(type, device, cell);
        Assert.Empty(recorder.Unexpected);
        return args;
    }
}
