using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Creates an argument source for one device, from build capabilities and device-open traits.</summary>
public interface IArgumentSourceFactory
{
    /// <summary>Creates the argument source.</summary>
    /// <param name="capabilities">Build capabilities.</param>
    /// <param name="traits">What opening the device revealed.</param>
    /// <returns>An argument source configured for that device.</returns>
    IArgumentSource Create(FfmpegCapabilities capabilities, DeviceTraits traits);
}
