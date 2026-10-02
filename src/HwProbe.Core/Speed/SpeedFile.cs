namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>A real video file to measure, described as Jellyfin describes its streams.</summary>
/// <param name="Path">The file.</param>
/// <param name="Name">What to call it, e.g. the movie's name.</param>
/// <param name="Duration">Its length, to start a tenth of the way in.</param>
/// <param name="Video">Its video stream.</param>
public sealed record SpeedFile(string Path, string Name, TimeSpan Duration, SpeedFileVideo Video)
{
    /// <summary>Gets its audio stream, or null when it has none.</summary>
    public SpeedFileAudio? Audio { get; init; }

    /// <summary>Gets the media source ID Jellyfin knows it by, or null outside the server.</summary>
    public string? MediaSourceId { get; init; }

    /// <summary>Gets its subtitle streams, internal and external.</summary>
    public IReadOnlyList<SpeedFileSubtitle> Subtitles { get; init; } = [];
}
