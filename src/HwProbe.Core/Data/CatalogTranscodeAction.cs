using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>What a performance test can do when the server starts transcoding, as the page lists it.</summary>
public sealed class CatalogTranscodeAction
{
    /// <summary>Gets the action.</summary>
    public required TranscodeAction Key { get; init; }

    /// <summary>Gets what the select shows.</summary>
    public required string Label { get; init; }
}
