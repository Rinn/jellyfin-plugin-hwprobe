namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>One speed test, as the page lists it.</summary>
/// <param name="Key">The test key.</param>
/// <param name="Label">What it measures.</param>
/// <param name="DecodeOnly">Whether it only decodes, so it reports fps but no stream count.</param>
/// <param name="Interlaced">Whether its source is interlaced, so the deinterlace comparison applies.</param>
/// <param name="Default">Whether it's chosen when the page first loads.</param>
/// <param name="FrameRate">The source frame rate, so fps can be shown as a multiple of real time.</param>
/// <param name="Credit">The credit a sample's licence requires, or null.</param>
public sealed record SpeedTestInfo(string Key, string Label, bool DecodeOnly, bool Interlaced, bool Default, float FrameRate, string? Credit)
{
    /// <summary>Returns the page's view of a test.</summary>
    /// <param name="test">The test.</param>
    /// <returns>The view.</returns>
    public static SpeedTestInfo From(Core.Speed.SpeedTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return new(test.Key, test.Label, test.DecodeOnly, test.Interlaced, Core.Speed.SpeedCatalog.Default.Contains(test.Key), test.FrameRate, test.Credit);
    }
}
