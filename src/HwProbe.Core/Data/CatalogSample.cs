namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>Where a downloaded sample comes from and the credit its licence requires.</summary>
internal sealed class CatalogSample
{
    /// <summary>Gets the film's title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets who holds the rights, with the licence, as the page shows it beside the title, e.g. <c>Blender Foundation (mango.blender.org), CC BY 3.0</c>.</summary>
    public required string Holder { get; init; }

    /// <summary>Gets the rights holder's site, linked where <see cref="Holder"/> names its host, or null.</summary>
    public string? HolderUrl { get; init; }

    /// <summary>Gets the credit.</summary>
    public required string Credit { get; init; }

    /// <summary>Gets the licence's URL.</summary>
    public required string License { get; init; }

    /// <summary>Gets the page the file comes from, with its credit and licence.</summary>
    public required string Source { get; init; }

    /// <summary>Gets the Wikipedia article about the film.</summary>
    public required string Article { get; init; }
}
