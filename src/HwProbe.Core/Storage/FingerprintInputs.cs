namespace Jellyfin.Plugin.HwProbe.Core.Storage;

/// <summary>The six fingerprint components, in hashing order; a null field hashes as <c>unknown</c>.</summary>
/// <param name="FfmpegPath">Absolute ffmpeg path.</param>
/// <param name="FfmpegVersionLine">First line of <c>-version</c>.</param>
/// <param name="Hwaccels">The <c>-hwaccels</c> list.</param>
/// <param name="Devices">Device list from enumeration (render nodes, adapter indices).</param>
/// <param name="DeviceIdentities">Per-device identity: Linux sysfs vendor/device + VAAPI driver, Windows adapter description + vendor ID.</param>
/// <param name="KernelRelease">macOS <c>uname -r</c>; null elsewhere unless known.</param>
/// <param name="OsPlatform">OS platform name.</param>
/// <param name="OsVersion">Kernel or OS version.</param>
public sealed record FingerprintInputs(
    string? FfmpegPath,
    string? FfmpegVersionLine,
    IReadOnlyList<string>? Hwaccels,
    IReadOnlyList<string>? Devices,
    IReadOnlyDictionary<string, string?>? DeviceIdentities,
    string? KernelRelease,
    string? OsPlatform,
    string? OsVersion)
{
    /// <summary>Gets the identity of the hwprobe build, so a changed build never reuses an older build's report; omitted when null.</summary>
    public string? ToolBuild { get; init; }
}
