namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>The subtitle files the burn-in variation reads.</summary>
internal sealed class CatalogSubtitles
{
    /// <summary>Gets the text (ASS) subtitle.</summary>
    public required CatalogClip Text { get; init; }

    /// <summary>Gets the image (PGS) subtitle.</summary>
    public required CatalogClip Image { get; init; }
}
