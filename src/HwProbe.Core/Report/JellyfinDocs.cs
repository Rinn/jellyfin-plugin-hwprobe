namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>Jellyfin's hardware acceleration guides, by section.</summary>
/// <remarks>URLs and anchors checked against jellyfin.org on 2026-10-01.</remarks>
public static class JellyfinDocs
{
    /// <summary>The hardware acceleration guides' common prefix.</summary>
    public const string Base = "https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/";

    /// <summary>Returns the guide for a vendor.</summary>
    /// <param name="vendor"><c>intel</c>, <c>nvidia</c> or <c>amd</c>; empty for the overview.</param>
    /// <param name="anchor">A section anchor such as <c>official-docker</c>, or null.</param>
    /// <returns>The URL.</returns>
    public static Uri Guide(string vendor, string? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        return new(Base + (vendor.Length == 0 ? string.Empty : vendor + "/") + (anchor is null ? string.Empty : "#" + anchor));
    }
}
