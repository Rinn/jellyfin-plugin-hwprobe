namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>An audio input, as the catalog file writes it.</summary>
internal sealed class CatalogAudio
{
    /// <summary>Gets the stable key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the page calls it, e.g. <c>FLAC</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what the page adds to the name, e.g. <c>16-bit</c>.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the codec as Jellyfin reports it.</summary>
    public required string Codec { get; init; }

    /// <summary>Gets the channel count.</summary>
    public required int Channels { get; init; }

    /// <summary>Gets the sample rate.</summary>
    public required int SampleRate { get; init; }

    /// <summary>Gets a value indicating whether it's an old format, listed last.</summary>
    public bool Legacy { get; init; }

    /// <summary>Gets the clip.</summary>
    public required CatalogClip Clip { get; init; }
}
