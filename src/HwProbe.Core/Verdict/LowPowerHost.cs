using Jellyfin.Plugin.HwProbe.Core.Devices;

namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>The host facts a failed low-power encode's remedy depends on.</summary>
/// <param name="Os">The host OS.</param>
/// <param name="InContainer">Whether the probe ran in a container, where firmware and driver options belong to the host.</param>
/// <param name="Synology">Whether the host runs Synology DSM, which has no firmware package or initramfs.</param>
/// <param name="I915Loaded">Whether the i915 driver is loaded.</param>
/// <param name="EnableGuc">The i915 <c>enable_guc</c> value, or null when it can't be read.</param>
public sealed record LowPowerHost(HostOs Os, bool InContainer, bool Synology, bool I915Loaded, string? EnableGuc);
