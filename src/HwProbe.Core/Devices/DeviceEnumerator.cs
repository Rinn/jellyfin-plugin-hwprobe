using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>Lists the devices to try per backend.</summary>
public sealed class DeviceEnumerator
{
    /// <summary>The literal recorded for an unreadable fingerprint field.</summary>
    public const string Unknown = "unknown";

    /// <summary>How many Windows adapter indices are tried; more adapters are rare and each missing one costs a launch.</summary>
    public const int AdapterCount = 4;

    private const string DriDirectory = "/dev/dri";

    private readonly IHostPlatform _platform;

    /// <summary>Initializes a new instance of the <see cref="DeviceEnumerator"/> class.</summary>
    /// <param name="platform">Host access.</param>
    public DeviceEnumerator(IHostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
    }

    /// <summary>Enumerates candidates for every backend that applies to this OS.</summary>
    /// <returns>The candidates and render-node facts.</returns>
    /// <remarks>CUDA has no enumeration API, so nvenc yields indices 0..3; probing stops at the first that fails to open.</remarks>
    public DeviceEnumeration Enumerate()
    {
        var listing = _platform.Os == HostOs.Linux ? _platform.ListDirectory(DriDirectory, "renderD*") : null;
        var nodes = listing is { Access: DirectoryAccess.Ok } ? SortNodes(listing.Entries) : [];
        List<string> indices = [.. Enumerable.Range(0, AdapterCount).Select(i => i.ToString(CultureInfo.InvariantCulture))];

        List<DeviceCandidate> candidates = [];
        foreach (var type in Enum.GetValues<HwType>())
        {
            candidates.AddRange(DevicesFor(type, nodes, indices).Select(d => new DeviceCandidate(type, d)));
        }

        return new DeviceEnumeration(candidates, listing?.Access, [.. nodes.Select(Identify)]);
    }

    /// <summary>Orders render nodes by numeric suffix, so renderD1000 sorts after renderD129.</summary>
    /// <param name="entries">Paths from the directory listing.</param>
    /// <returns>The sorted paths.</returns>
    private static List<string> SortNodes(IReadOnlyList<string> entries) =>
        [.. entries
            .OrderBy(p => int.TryParse(Path.GetFileName(p)["renderD".Length..], CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
            .ThenBy(p => p, StringComparer.Ordinal)];

    /// <summary>Returns the device selectors one backend takes on this OS.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="nodes">Render nodes found on Linux.</param>
    /// <param name="indices">Adapter indices 0..3.</param>
    /// <returns>The selectors; empty when the backend doesn't apply here.</returns>
    private List<string> DevicesFor(HwType type, List<string> nodes, List<string> indices) => (type, _platform.Os) switch
    {
        (HwType.vaapi or HwType.qsv, HostOs.Linux) => nodes,
        (HwType.qsv or HwType.amf, HostOs.Windows) => indices,
        (HwType.nvenc, HostOs.Linux or HostOs.Windows) => indices,
        (HwType.videotoolbox, HostOs.MacOS) => [string.Empty],

        // v4l2m2m has no device init upstream; its single candidate gets no device open.
        (HwType.rkmpp or HwType.v4l2m2m, HostOs.Linux) => [string.Empty],
        _ => [],
    };

    /// <summary>Reads a render node's PCI vendor and device IDs from sysfs.</summary>
    /// <param name="node">The render node path.</param>
    /// <returns>The identity, with <see cref="Unknown"/> for unreadable fields.</returns>
    private RenderNodeIdentity Identify(string node)
    {
        var sysfs = $"/sys/class/drm/{Path.GetFileName(node)}/device";
        return new RenderNodeIdentity(node, ReadField($"{sysfs}/vendor"), ReadField($"{sysfs}/device"));
    }

    /// <summary>Reads one trimmed sysfs value.</summary>
    /// <param name="path">The sysfs file.</param>
    /// <returns>The value, or <see cref="Unknown"/> when missing, unreadable or blank.</returns>
    private string ReadField(string path)
    {
        var value = _platform.TryReadText(path)?.Trim();
        return string.IsNullOrEmpty(value) ? Unknown : value;
    }
}
