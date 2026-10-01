using Jellyfin.Plugin.HwProbe.Core.Pipeline;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Per-device facts learned when opening the device that change the args upstream generates.</summary>
/// <param name="Driver">The VAAPI driver, or <see cref="VaapiDriver.Other"/> when unknown or not VAAPI.</param>
public sealed record DeviceTraits(VaapiDriver Driver);
