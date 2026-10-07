namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>An audio output, as the catalog file writes it.</summary>
internal sealed class CatalogAudioOutput
{
    /// <summary>Gets the stable key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the page calls it.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the codec a client asks for, or null to decode only.</summary>
    public string? Codec { get; init; }

    /// <summary>Gets the channels a client asks for.</summary>
    public int Channels { get; init; } = 2;
}
