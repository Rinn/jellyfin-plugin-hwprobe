using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>Builds an <see cref="ArgumentSource"/> per device from build capabilities and device-open traits.</summary>
public sealed class ArgumentSourceFactory : IArgumentSourceFactory
{
    /// <summary>Maps build enumeration output onto the stub IMediaEncoder's data.</summary>
    /// <param name="capabilities">Build capabilities.</param>
    /// <param name="traits">Device-open traits.</param>
    /// <returns>The probe capabilities.</returns>
    public static ProbeCapabilities ToProbeCapabilities(FfmpegCapabilities capabilities, DeviceTraits traits)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(traits);

        // Build enumeration keys filter options by FilterOptionType member name.
        var options = capabilities.FilterOptions
            .Where(o => o.Value)
            .Select(o => Enum.TryParse<FilterOptionType>(o.Key, out var type) ? type : (FilterOptionType?)null)
            .OfType<FilterOptionType>()
            .ToHashSet();

        return new ProbeCapabilities
        {
            EncoderPath = capabilities.Path,
            EncoderVersion = capabilities.Version ?? new Version(0, 0),
            Hwaccels = capabilities.Hwaccels,
            Encoders = capabilities.Encoders,
            Decoders = capabilities.Decoders,
            Filters = capabilities.Filters,
            FilterOptions = options,
            IsVaapiDeviceInteliHD = traits.Driver == VaapiDriver.IntelIhd,
            IsVaapiDeviceInteli965 = traits.Driver == VaapiDriver.IntelI965,
            IsVaapiDeviceAmd = traits.Driver == VaapiDriver.Amd,
        };
    }

    /// <inheritdoc/>
    public IArgumentSource Create(FfmpegCapabilities capabilities, DeviceTraits traits)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(traits);

        return new ArgumentSource(ToProbeCapabilities(capabilities, traits), new CallRecorder()) { LowPriorityHwDecode = capabilities.LowPriorityHwDecode };
    }
}
