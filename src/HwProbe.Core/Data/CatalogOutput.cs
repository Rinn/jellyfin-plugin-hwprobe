namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>A speed run output, as the catalog file writes it.</summary>
internal sealed class CatalogOutput
{
    /// <summary>Gets the stable key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the page calls it.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the output codec, or null to decode only.</summary>
    public string? Codec { get; init; }

    /// <summary>Gets the output height.</summary>
    public int Height { get; init; }

    /// <summary>Gets the video bitrate.</summary>
    public int Bitrate { get; init; }

    /// <summary>Gets the lowest and highest bitrate for the height, or empty to decode only.</summary>
    public IReadOnlyList<int> BitrateRange { get; init; } = [];

    /// <summary>Gets what's made.</summary>
    public required string Detail { get; init; }
}
