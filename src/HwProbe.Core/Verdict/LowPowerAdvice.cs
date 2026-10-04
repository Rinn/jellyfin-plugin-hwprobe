using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>Advice for Jellyfin's two Intel Low-Power encoder options, from the low-power and normal encode results.</summary>
/// <remarks>
/// Facts from Jellyfin's Intel guide (jellyfin.org, docs/general/post-install/transcoding/hardware-acceleration/intel.md,
/// "Low-Power Encoding"): Jasper Lake, Elkhart Lake, Arc (DG2) and newer only have low-power encoders; Linux supports
/// low-power by default only on Gen 12 ADL and newer, and older i915 GPUs need the HuC firmware loaded with
/// enable_guc=2. DG1, 12th-gen and newer, and Arc A-series default to enable_guc=3; the xe driver needs neither.
/// </remarks>
public static class LowPowerAdvice
{
    /// <summary>Path of the i915 driver's GuC/HuC loading parameter; readable without root.</summary>
    public const string EnableGucPath = "/sys/module/i915/parameters/enable_guc";

    /// <summary>Jellyfin's guide to setting up low-power mode on Linux (<c>intelLowPowerGuide</c> in catalog.yaml).</summary>
    public static readonly Uri Guide = new(Data.Catalog.Default.Links["intelLowPowerGuide"]);

    /// <summary>Gets the remedy when the encoder opened in low-power mode but dropped it for Jellyfin's settings, which no firmware change helps.</summary>
    /// <remarks>jellyfin-ffmpeg's qsvenc turns low power off when "some encoding parameters are not supported under Low power mode" (debian/patches/0071). On a Gen 9 (Apollo Lake) NAS it was the bitrate target.</remarks>
    public static string Dropped => "Low power doesn't work with the bitrate Jellyfin sets, so ffmpeg uses normal mode.";

    /// <summary>Returns the remedy for a codec whose low-power encode fails.</summary>
    /// <param name="codec">The output codec, e.g. <c>hevc</c>.</param>
    /// <param name="os">The host OS.</param>
    /// <param name="inContainer">Whether the probe ran in a container.</param>
    /// <param name="enableGuc">The i915 <c>enable_guc</c> value, or null when the i915 driver isn't loaded.</param>
    /// <param name="support">The low-power encoders the device's generation has.</param>
    /// <returns>Remedy text.</returns>
    /// <remarks>From Jellyfin's Intel guide, which says Gen 9.x graphics support "non-LP and LP (H.264 only) encoding".</remarks>
    public static string Remedy(string codec, HostOs os, bool inContainer, string? enableGuc, LowPowerSupport support) =>
        support == LowPowerSupport.None
            ? "This GPU is Gen 8 Intel graphics or older, which has no low-power encoders, so this is expected and no firmware change helps."
        : codec == "hevc" && support == LowPowerSupport.H264Only
            ? "This GPU is Gen 9 Intel graphics, which has low-power H.264 only, so this is expected and no firmware change helps."
        : codec == "hevc"
            ? "Gen 9 Intel graphics (Skylake to Comet Lake, Apollo Lake, Gemini Lake) have low-power H.264 only, so there this is expected and no firmware change helps. On newer GPUs: " + Remedy(os, inContainer, enableGuc)
            : Remedy(os, inContainer, enableGuc);

    /// <summary>Returns the remedy for a failed low-power encode.</summary>
    /// <param name="os">The host OS.</param>
    /// <param name="inContainer">Whether the probe ran in a container, where firmware and driver options belong to the host.</param>
    /// <param name="enableGuc">The i915 <c>enable_guc</c> value, or null when the i915 driver isn't loaded.</param>
    /// <returns>Remedy text.</returns>
    public static string Remedy(HostOs os, bool inContainer, string? enableGuc)
    {
        if (os != HostOs.Linux)
        {
            return "This GPU's driver doesn't support low-power encoding here. Leave the Intel Low-Power encoder option off.";
        }

        if (enableGuc is null)
        {
            return $"Low-power encoding failed, and the i915 driver isn't loaded (the xe driver requires no firmware option). Check the Intel firmware package is installed. Guide: {Guide}";
        }

        var where = inContainer ? "On the host (not in the container), install" : "Install";
        return $"Low-power encoding on Linux requires Intel's HuC firmware. {where} the firmware package (firmware-intel-graphics on Debian, "
            + "linux-firmware on Ubuntu and Arch). Except on DG1, 12th-gen and newer, or Arc A-series GPUs, which already default to enable_guc=3, "
            + $"add 'options i915 enable_guc=2' to /etc/modprobe.d/i915.conf, update the initramfs, and reboot. enable_guc is currently {enableGuc}. Guide: {Guide}";
    }

    /// <summary>Returns findings for the low-power encoder options of one Intel device.</summary>
    /// <param name="type">The backend (QSV or VAAPI).</param>
    /// <param name="device">The device.</param>
    /// <param name="encode">Encode results by key, including <c>h264</c>, <c>hevc</c> and their <c>_lowpower</c> cells.</param>
    /// <param name="os">The host OS.</param>
    /// <param name="inContainer">Whether the probe ran in a container.</param>
    /// <param name="enableGuc">The i915 <c>enable_guc</c> value, or null.</param>
    /// <param name="support">The low-power encoders the device's generation has.</param>
    /// <param name="dropped">The output codecs whose low-power encode opened and then fell back to normal mode for Jellyfin's settings, or null.</param>
    /// <returns>One finding per codec that has both results.</returns>
    public static IEnumerable<Finding> Findings(HwType type, string device, IReadOnlyDictionary<string, ProbeOutcome> encode, HostOs os, bool inContainer, string? enableGuc, LowPowerSupport support, IReadOnlySet<string>? dropped = null)
    {
        ArgumentNullException.ThrowIfNull(encode);

        foreach (var (codec, option) in new[] { ("h264", "Intel Low-Power H.264 hardware encoder"), ("hevc", "Intel Low-Power HEVC hardware encoder") })
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
                yield return new Finding(FindingSeverity.Warn, $"enable-lowpower-{codec}", prefix + $"normal {codec} encoding fails but low-power works; this GPU only has low-power encoders. Turn on '{option}'.") { Backend = type };
            }
            else if (normalOk && lowPowerOk)
            {
                yield return new Finding(FindingSeverity.Info, $"lowpower-available-{codec}", prefix + $"low-power {codec} encoding works. Turning on '{option}' frees the GPU, which speeds up OpenCL tone-mapping.") { Backend = type };
            }
            else if (normalOk && !lowPowerOk)
            {
                // A generation without the encoder at all says so, whatever ffmpeg did with the option.
                var missing = support == LowPowerSupport.None || (codec == "hevc" && support == LowPowerSupport.H264Only);
                yield return dropped?.Contains(codec) == true && !missing
                    ? new Finding(FindingSeverity.Info, $"lowpower-dropped-{codec}", prefix + $"'{option}' has no effect: " + char.ToLowerInvariant(Dropped[0]) + Dropped[1..]) { Backend = type }
                    : new Finding(FindingSeverity.Info, $"lowpower-unavailable-{codec}", prefix + $"leave '{option}' off. " + Remedy(codec, os, inContainer, enableGuc, support)) { Backend = type };
            }
        }
    }
}
