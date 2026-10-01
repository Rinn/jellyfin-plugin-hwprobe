namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>The result of listing a directory.</summary>
/// <param name="Access">Whether the listing succeeded.</param>
/// <param name="Entries">Full paths of matching entries; empty unless <see cref="DirectoryAccess.Ok"/>.</param>
public sealed record DirectoryListing(DirectoryAccess Access, IReadOnlyList<string> Entries);
