using System.Text.RegularExpressions;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Diagnostics;

/// <summary>Builds the link that opens a hardware report on GitHub with what the probe already knows filled in.</summary>
/// <remarks>GitHub issue forms fill a field from a query parameter named after the field's id (.github/ISSUE_TEMPLATE/hardware-report.yml).</remarks>
public static partial class IssueLink
{
    // Microsoft's PCI vendor ID: Windows lists its software adapter, Microsoft Basic Render Driver, after the GPUs.
    private const string SoftwareAdapterVendor = "0x1414";

    /// <summary>Gets the issue form, empty.</summary>
    public static string Form => Data.Catalog.Default.Links["issueForm"];

    /// <summary>Returns the issue form's link with the host, versions, and results filled in.</summary>
    /// <param name="report">The probe's report.</param>
    /// <param name="jellyfinVersion">The server's Jellyfin version, or null outside the plugin.</param>
    /// <returns>The link.</returns>
    public static string For(CapabilityReport report, string? jellyfinVersion)
    {
        ArgumentNullException.ThrowIfNull(report);
        var host = report.Host;
        var fields = new List<(string Id, string Value)>();
        var gpus = report.Gpus.Where(g => g.Vendor != SoftwareAdapterVendor).Select(Describe).Distinct(StringComparer.Ordinal).ToList();
        if (gpus.Count > 0)
        {
            fields.Add(("gpu", string.Join("; ", gpus)));
        }

        fields.Add(("os", $"{host.Os} {host.Kernel} {host.Architecture}, {host.Container ?? "not in a container"}"));
        fields.Add(("hwprobe", report.HwProbeVersion));
        fields.Add(("ffmpeg", $"{report.Ffmpeg.Version}, {(report.Ffmpeg.IsJellyfinBuild ? "jellyfin-ffmpeg" : "not jellyfin-ffmpeg")}"));
        if (jellyfinVersion is not null)
        {
            fields.Add(("jellyfin", jellyfinVersion));
        }

        var working = report.Backends.Where(b => b.Verdict == Model.BackendVerdict.Viable).Select(b => b.Type.ToString()).Distinct().ToList();
        fields.Add(("backends", working.Count > 0 ? string.Join(", ", working) : "none"));
        return Form + string.Concat(fields.Select(f => "&" + f.Id + "=" + Uri.EscapeDataString(f.Value)));
    }

    /// <summary>Names a GPU for the form: the adapter's own name on Windows, the model Mesa's VA-API driver names on Linux, else its maker, PCI IDs, and driver line.</summary>
    /// <param name="gpu">The GPU.</param>
    /// <returns>e.g. <c>NVIDIA GeForce RTX 5080</c>, <c>AMD Radeon RX 7600 (1002:7480)</c>, or <c>Intel 8086:5a85, Intel iHD driver for Intel(R) Gen Graphics - 24.1.0</c>.</returns>
    private static string Describe(GpuInfo gpu)
    {
        if (gpu.Device.StartsWith("dx11:", StringComparison.Ordinal) && !string.IsNullOrEmpty(gpu.Name))
        {
            return gpu.Name;
        }

        var ids = Hex(gpu.Vendor) + ":" + Hex(gpu.Id);
        if (gpu.Name is { } driver && MesaModel().Match(driver) is { Success: true } mesa)
        {
            return $"{mesa.Groups[1].Value} ({ids})";
        }

        var maker = Data.Catalog.Default.PciVendors.FirstOrDefault(v => string.Equals(v.Key, gpu.Vendor, StringComparison.OrdinalIgnoreCase)).Value;
        var named = maker is null ? ids : maker + " " + ids;
        return string.IsNullOrEmpty(gpu.Name) ? named : named + ", " + BuildHash().Replace(gpu.Name, string.Empty);
    }

    /// <summary>Drops the <c>0x</c> from a PCI ID.</summary>
    /// <param name="id">The ID, e.g. <c>0x8086</c>.</param>
    /// <returns>e.g. <c>8086</c>.</returns>
    private static string Hex(string id) => id.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? id[2..] : id;

    /// <summary>Matches the model in Mesa's VA-API driver line, e.g. "Mesa Gallium driver 24.0.5 for AMD Radeon RX 7600 (radeonsi, navi33, ...)".</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"^Mesa Gallium driver \S+ for (.+?) \(")]
    private static partial Regex MesaModel();

    /// <summary>Matches the build hash Intel's media driver adds to its line, e.g. " (1b5e662)".</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@" \([0-9a-f]{7,}\)$")]
    private static partial Regex BuildHash();
}
