using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Diagnostics;

/// <summary>Builds the link that opens a hardware report on GitHub with what the probe already knows filled in.</summary>
/// <remarks>GitHub issue forms fill a field from a query parameter named after the field's id (.github/ISSUE_TEMPLATE/hardware-report.yml).</remarks>
public static class IssueLink
{
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
        var fields = new List<(string Id, string Value)>
        {
            ("os", $"{host.Os} {host.Kernel} {host.Architecture}, {host.Container ?? "not in a container"}"),
            ("hwprobe", report.HwProbeVersion),
            ("ffmpeg", $"{report.Ffmpeg.Version} ({report.Ffmpeg.Path})"),
        };
        if (jellyfinVersion is not null)
        {
            fields.Add(("jellyfin", jellyfinVersion));
        }

        var working = report.Backends.Where(b => b.Verdict == Model.BackendVerdict.Viable).Select(b => b.Type.ToString()).Distinct().ToList();
        fields.Add(("backends", working.Count > 0 ? string.Join(", ", working) : "none"));
        return Form + string.Concat(fields.Select(f => "&" + f.Id + "=" + Uri.EscapeDataString(f.Value)));
    }
}
