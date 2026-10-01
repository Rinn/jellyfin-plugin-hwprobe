using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>The device-open outcome and the driver it identified.</summary>
/// <param name="Outcome">Pass, or why the device didn't open.</param>
/// <param name="Driver">The VAAPI driver named in stderr, when there is one.</param>
/// <param name="DriverDescription">The driver line from stderr, for the fingerprint; null when absent.</param>
public sealed record DeviceOpenResult(ProbeOutcome Outcome, VaapiDriver Driver, string? DriverDescription);
