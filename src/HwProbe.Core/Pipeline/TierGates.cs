using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Pipeline;

/// <summary>Every input upstream consults when choosing a VAAPI or QSV filter pipeline.</summary>
/// <param name="Backend">The selected hardware acceleration type.</param>
/// <param name="Platform">The host OS.</param>
/// <param name="HasHardwareCodec">The job's decoder or encoder belongs to the backend.</param>
/// <param name="InputIsMpeg4">The input is MPEG-4 Part 2, which <c>IsVaapiSupported</c> rejects.</param>
/// <param name="HwaccelVaapi">The build lists the <c>vaapi</c> hwaccel.</param>
/// <param name="HwaccelQsv">The build lists the <c>qsv</c> hwaccel.</param>
/// <param name="HwaccelD3d11va">The build lists the <c>d3d11va</c> hwaccel.</param>
/// <param name="VaapiFull">Upstream's <c>IsVaapiFullSupported</c>.</param>
/// <param name="OpenclFull">Upstream's <c>IsOpenclFullSupported</c>.</param>
/// <param name="VulkanFull">Upstream's <c>IsVulkanFullSupported</c>.</param>
/// <param name="Alphasrc">The build has the <c>alphasrc</c> filter.</param>
/// <param name="Driver">The VA-API driver family.</param>
/// <param name="VulkanDrmInterop">Upstream's <c>IsVaapiDeviceSupportVulkanDrmInterop</c>.</param>
/// <param name="KernelVersion">The host kernel version (<c>Environment.OSVersion.Version</c> on Linux).</param>
public sealed record TierGates(
    HwType Backend,
    HostOs Platform,
    bool HasHardwareCodec,
    bool InputIsMpeg4,
    bool HwaccelVaapi,
    bool HwaccelQsv,
    bool HwaccelD3d11va,
    bool VaapiFull,
    bool OpenclFull,
    bool VulkanFull,
    bool Alphasrc,
    VaapiDriver Driver,
    bool VulkanDrmInterop,
    Version KernelVersion);
