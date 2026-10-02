namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A speed run video, as the catalog file writes it.</summary>
internal sealed class CatalogVideo
{
    /// <summary>Gets the stable key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the page calls it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the frame rate.</summary>
    public required float FrameRate { get; init; }

    /// <summary>Gets the width.</summary>
    public required int Width { get; init; }

    /// <summary>Gets the height.</summary>
    public required int Height { get; init; }

    /// <summary>Gets the audio as the page describes it.</summary>
    public required string Audio { get; init; }

    /// <summary>Gets the clip.</summary>
    public required CatalogClip Clip { get; init; }

    /// <summary>Gets where a downloaded sample comes from, or null for a generated video.</summary>
    public CatalogSample? Sample { get; init; }
}
