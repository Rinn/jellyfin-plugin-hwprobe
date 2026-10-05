using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Devices;

namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>The host facts a failed low-power encode's remedy depends on.</summary>
/// <param name="Os">The host OS.</param>
/// <param name="InContainer">Whether the probe ran in a container, where firmware and driver options belong to the host.</param>
/// <param name="I915Loaded">Whether the i915 driver is loaded.</param>
/// <param name="EnableGuc">The i915 <c>enable_guc</c> value, or null when it can't be read.</param>
public sealed record LowPowerHost(HostOs Os, bool InContainer, bool I915Loaded, string? EnableGuc)
{
    /// <summary>Returns whether i915 is set to load HuC (the bitmask value 2, per the kernel's i915_params.c); a requested load can still fail.</summary>
    /// <param name="support">The low-power encoders the device's generation has.</param>
    /// <returns>True or false when known; null when enable_guc is unreadable, or -1 on a generation whose default isn't known here.</returns>
    /// <remarks>Since kernel 5.x, uc_expand_default_options (intel_uc.c) resolves -1 into a per-device copy, so sysfs keeps showing -1. The default is off before Gen 12, so -1 on a known Gen 9 or older GPU means no HuC.</remarks>
    public bool? HucRequested(LowPowerSupport support) =>
        !int.TryParse(EnableGuc, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? null
        : value >= 0 ? (value & 2) != 0
        : support is LowPowerSupport.None or LowPowerSupport.H264Only ? false
        : null;
}
