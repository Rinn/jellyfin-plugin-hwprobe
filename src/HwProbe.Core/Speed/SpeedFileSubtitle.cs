namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>A file's subtitle stream, or a subtitle file beside it.</summary>
/// <param name="Index">The stream index Jellyfin gives it.</param>
/// <param name="Codec">The codec as Jellyfin names it, e.g. <c>PGSSUB</c> or <c>subrip</c>.</param>
/// <param name="IsText">Whether it's text rather than images.</param>
/// <param name="Title">The language or title to show, or null.</param>
public sealed record SpeedFileSubtitle(int Index, string Codec, bool IsText, string? Title)
{
    /// <summary>Gets the subtitle file's path when it's external, or null for a track inside the video file.</summary>
    public string? ExternalPath { get; init; }
}
