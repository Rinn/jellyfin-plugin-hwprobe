using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A backend as Jellyfin's Hardware acceleration dropdown names it.</summary>
public sealed class CatalogBackend
{
    /// <summary>Gets the backend.</summary>
    public required HwType Type { get; init; }

    /// <summary>Gets the dropdown's name for it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the operating systems Jellyfin's ffmpeg builds it for, as the report names them.</summary>
    public required IReadOnlyList<string> Platforms { get; init; }

    /// <summary>Gets what to show on other operating systems, e.g. <c>Linux only</c>.</summary>
    public required string PlatformNote { get; init; }

    /// <summary>Gets what to show on hosts that aren't ARM, or null when it isn't ARM only.</summary>
    public string? ArmNote { get; init; }
}
