using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>Advice for Jellyfin's two Intel Low-Power encoder options, from the low-power and normal encode results.</summary>
/// <remarks>
/// Facts from Jellyfin's Intel guide (jellyfin.org, docs/general/post-install/transcoding/hardware-acceleration/intel.md,
/// "Low-Power Encoding"): Jasper Lake, Elkhart Lake, Arc (DG2) and newer only have low-power encoders; Linux supports
/// low-power by default only on Gen 12 ADL and newer, and older i915 GPUs need the HuC firmware loaded with
/// enable_guc=2. DG1, 12th-gen and newer, and Arc A-series load HuC by default (enable_guc=3, or 2 on Alder Lake-S, per the
/// kernel's intel_uc.c uc_expand_default_options); the xe driver needs neither.
/// </remarks>
public static class LowPowerAdvice
{
    /// <summary>Path of the i915 driver's GuC/HuC loading parameter; root-only on Synology DSM.</summary>
    public const string EnableGucPath = "/sys/module/i915/parameters/enable_guc";

    /// <summary>Jellyfin's Intel guide links here for missing GuC and HuC files (<c>linuxFirmwareI915</c> in catalog.yaml).</summary>
    public static readonly Uri FirmwareFiles = new(Data.Catalog.Default.Links["linuxFirmwareI915"]);

    /// <summary>Jellyfin's guide to setting up low-power mode on Linux (<c>intelLowPowerGuide</c> in catalog.yaml).</summary>
    public static readonly Uri Guide = new(Data.Catalog.Default.Links["intelLowPowerGuide"]);

    /// <summary>Gets the remedy when the encoder opened in low-power mode but dropped it for Jellyfin's settings, which no firmware change helps.</summary>
    /// <remarks>jellyfin-ffmpeg's qsvenc turns low power off when "some encoding parameters are not supported under Low power mode" (debian/patches/0071). On a Gen 9 (Apollo Lake) NAS it was the bitrate target.</remarks>
    public static string Dropped => Catalog.Text("lowPowerDropped");

    /// <summary>Returns the remedy for a codec whose low-power encode fails.</summary>
    /// <param name="codec">The output codec, e.g. <c>hevc</c>.</param>
    /// <param name="host">The host facts.</param>
    /// <param name="support">The low-power encoders the device's generation has.</param>
    /// <returns>Remedy text.</returns>
    /// <remarks>From Jellyfin's Intel guide, which says Gen 9.x graphics support "non-LP and LP (H.264 only) encoding".</remarks>
    public static string Remedy(string codec, LowPowerHost host, LowPowerSupport support) =>
        support == LowPowerSupport.None ? Catalog.Text("lowPowerNone")
        : codec == "hevc" && support == LowPowerSupport.H264Only ? Catalog.Text("lowPowerHevcGen9")
        : codec == "hevc" ? Catalog.Text("lowPowerHevcMaybeGen9", ("remedy", Remedy(host)))
        : Remedy(host);

    /// <summary>Returns the remedy for a failed low-power encode.</summary>
    /// <param name="host">The host facts.</param>
    /// <returns>Remedy text.</returns>
    public static string Remedy(LowPowerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Os != HostOs.Linux)
        {
            return Catalog.Text("lowPowerNotLinux");
        }

        if (!host.I915Loaded)
        {
            return Catalog.Text("lowPowerNoI915", ("guide", Guide.ToString()));
        }

        var current = host.EnableGuc is null ? Catalog.Text("lowPowerEnableGucRootOnly", ("path", EnableGucPath)) : Catalog.Text("lowPowerEnableGuc", ("value", host.EnableGuc));
        return host.Synology
            ? Catalog.Text("lowPowerFirmwareSynology", ("where", Catalog.Text(host.InContainer ? "lowPowerSynologyContainer" : "lowPowerSynologyHost")), ("firmware", FirmwareFiles.ToString()), ("current", current), ("guide", Guide.ToString()))
            : Catalog.Text("lowPowerFirmware", ("where", Catalog.Text(host.InContainer ? "lowPowerFirmwareContainer" : "lowPowerFirmwareHost")), ("current", current), ("guide", Guide.ToString()));
    }

    /// <summary>Returns findings for the low-power encoder options of one Intel device.</summary>
    /// <param name="type">The backend (QSV or VAAPI).</param>
    /// <param name="device">The device.</param>
    /// <param name="encode">Encode results by key, including <c>h264</c>, <c>hevc</c> and their <c>_lowpower</c> cells.</param>
    /// <param name="host">The host facts.</param>
    /// <param name="support">The low-power encoders the device's generation has.</param>
    /// <param name="dropped">The output codecs whose low-power encode opened and then fell back to normal mode for Jellyfin's settings, or null.</param>
    /// <returns>One finding per codec that has both results.</returns>
    public static IEnumerable<Finding> Findings(HwType type, string device, IReadOnlyDictionary<string, ProbeOutcome> encode, LowPowerHost host, LowPowerSupport support, IReadOnlySet<string>? dropped = null)
    {
        ArgumentNullException.ThrowIfNull(encode);

        foreach (var (codec, option) in new[] { ("h264", Catalog.Text("lowPowerOptionH264")), ("hevc", Catalog.Text("lowPowerOptionHevc")) })
        {
            // Skipped means Jellyfin ignores the option here, so there is nothing to advise.
            if (!encode.TryGetValue(codec, out var normal) || !encode.TryGetValue(codec + "_lowpower", out var lowPower)
                || lowPower is ProbeOutcome.Skipped or ProbeOutcome.Untested)
            {
                continue;
            }

            var prefix = $"{type} {device}: ";
            var normalOk = normal == ProbeOutcome.Pass;
            var lowPowerOk = lowPower == ProbeOutcome.Pass;
            if (!normalOk && lowPowerOk)
            {
                yield return new Finding(FindingSeverity.Warn, $"enable-lowpower-{codec}", prefix + Catalog.Text("findingEnableLowPower", ("codec", codec), ("option", option))) { Backend = type };
            }
            else if (normalOk && lowPowerOk)
            {
                yield return new Finding(FindingSeverity.Info, $"lowpower-available-{codec}", prefix + Catalog.Text("findingLowPowerAvailable", ("codec", codec), ("option", option))) { Backend = type };
            }
            else if (normalOk && !lowPowerOk)
            {
                // A generation without the encoder at all says so, whatever ffmpeg did with the option.
                var missing = support == LowPowerSupport.None || (codec == "hevc" && support == LowPowerSupport.H264Only);
                yield return dropped?.Contains(codec) == true && !missing
                    ? new Finding(FindingSeverity.Info, $"lowpower-dropped-{codec}", prefix + Catalog.Text("findingLowPowerDropped", ("option", option), ("reason", Dropped))) { Backend = type }
                    : new Finding(FindingSeverity.Info, $"lowpower-unavailable-{codec}", prefix + Catalog.Text("findingLowPowerUnavailable", ("option", option), ("reason", Remedy(codec, host, support)))) { Backend = type };
            }
        }
    }
}
