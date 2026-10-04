using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>Remedy text per outcome, shown next to each failure in the report.</summary>
public static class Hints
{
    /// <summary>Gets the remedy for a host that resolved to the copy-back filter pipeline.</summary>
    public static string LegacyCopyBack => Catalog.Text("legacyCopyBack");

    /// <summary>Returns the remedy for an Intel device whose OpenCL runtime doesn't start.</summary>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>Remedy text.</returns>
    public static string OpenclUnavailable(bool inContainer) => Catalog.Text(inContainer ? "openclUnavailableContainer" : "openclUnavailableHost");

    /// <summary>Returns a short fix for a backend that doesn't work, when the user can act on it.</summary>
    /// <param name="verdict">The backend's verdict.</param>
    /// <param name="type">The backend.</param>
    /// <param name="os">The host OS.</param>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>The fix, or null when there's nothing to do (e.g. the hardware isn't there or the build lacks it).</returns>
    public static Fix? FixFor(BackendVerdict verdict, HwType type, HostOs os, bool inContainer) => (verdict, type) switch
    {
        (BackendVerdict.PermissionDenied, HwType.vaapi or HwType.qsv) when inContainer =>
            new(Catalog.Text("fixRenderGroupContainer"), Section(type, "official-docker")),
        (BackendVerdict.PermissionDenied, HwType.vaapi or HwType.qsv) =>
            new(Catalog.Text("fixRenderGroupHost"), Section(type, "configure-on-linux-host")),
        (BackendVerdict.NotPresent, HwType.nvenc) when inContainer =>
            new(Catalog.Text("fixGpusAll"), JellyfinDocs.Guide("nvidia", "official-docker")),
        (BackendVerdict.NotPresent, HwType.vaapi or HwType.qsv) when inContainer =>
            new(Catalog.Text("fixDeviceDri"), Section(type, "official-docker")),
        (BackendVerdict.NotPresent, HwType.vaapi or HwType.qsv) when os == HostOs.Linux =>
            new(Catalog.Text("fixLoadDriver"), Section(type, "configure-on-linux-host")),
        _ => null,
    };

    /// <summary>Returns the remedy when no listed Windows adapter is the backend's vendor.</summary>
    /// <param name="type">QSV or AMF.</param>
    /// <param name="device">The adapter index the user asked for, or null when every adapter was considered.</param>
    /// <returns>Remedy text.</returns>
    public static string NoVendorAdapter(HwType type, string? device)
    {
        var maker = type == HwType.qsv ? "Intel" : "AMD";
        return device is null ? Catalog.Text("noVendorAdapter", ("maker", maker)) : Catalog.Text("noVendorAdapterDevice", ("maker", maker), ("device", device));
    }

    /// <summary>Returns the fix for an Intel device whose OpenCL runtime doesn't start.</summary>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>The fix.</returns>
    public static Fix OpenclFix(bool inContainer) => inContainer
        ? new(Catalog.Text("fixOpenclContainer"), JellyfinDocs.Guide("intel", "official-docker"))
        : new(Catalog.Text("fixOpenclHost"), JellyfinDocs.Guide("intel", "configure-on-linux-host"));

    /// <summary>Returns the remedy for an outcome.</summary>
    /// <param name="outcome">The probe outcome.</param>
    /// <param name="type">The backend probed.</param>
    /// <param name="os">The host OS, so remedies name tools that exist there.</param>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>Remedy text; empty for a pass.</returns>
    public static string For(ProbeOutcome outcome, HwType type, HostOs os, bool inContainer) => outcome switch
    {
        ProbeOutcome.Pass => string.Empty,
        ProbeOutcome.PermissionDenied => PermissionDenied(type, inContainer),
        ProbeOutcome.DeviceUnavailable => DeviceUnavailable(type, os, inContainer),
        ProbeOutcome.CodecUnsupported => Catalog.Text("codecUnsupported"),
        ProbeOutcome.FilterUnsupported => Catalog.Text("filterUnsupported"),
        ProbeOutcome.Timeout => Catalog.Text("timeout"),
        ProbeOutcome.SoftwareFallback => Catalog.Text("softwareFallback"),
        ProbeOutcome.Skipped => Catalog.Text("skipped"),
        ProbeOutcome.Untested => Catalog.Text("untested"),
        ProbeOutcome.NotUsed => Catalog.Text("notUsed"),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>Returns a section of the guide for a backend.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="anchor">The section anchor in the Intel guide.</param>
    /// <returns>The Intel guide section for QSV; the overview for VAAPI, which serves both Intel and AMD and has no such sections.</returns>
    private static Uri Section(HwType type, string anchor) =>
        type == HwType.qsv ? JellyfinDocs.Guide("intel", anchor) : JellyfinDocs.Guide(string.Empty);

    /// <summary>Remedy for a device this user cannot open.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>Remedy text.</returns>
    private static string PermissionDenied(HwType type, bool inContainer) => type switch
    {
        HwType.vaapi or HwType.qsv when inContainer => Catalog.Text("permissionDeniedContainer"),
        HwType.vaapi or HwType.qsv => Catalog.Text("permissionDeniedHost"),
        _ => Catalog.Text("permissionDenied"),
    };

    /// <summary>Remedy for a device that could not be opened at all.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="os">The host OS.</param>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>Remedy text.</returns>
    private static string DeviceUnavailable(HwType type, HostOs os, bool inContainer) => type switch
    {
        HwType.qsv when os == HostOs.Windows => Catalog.Text("deviceUnavailableQsvWindows"),
        HwType.amf => Catalog.Text("deviceUnavailableAmf"),
        HwType.nvenc when inContainer => Catalog.Text("deviceUnavailableNvencContainer"),
        HwType.nvenc => Catalog.Text("deviceUnavailableNvenc"),
        HwType.vaapi or HwType.qsv when inContainer => Catalog.Text("deviceUnavailableNodeContainer"),
        HwType.vaapi or HwType.qsv => Catalog.Text("deviceUnavailableNode"),
        _ => Catalog.Text("deviceUnavailable"),
    };
}
