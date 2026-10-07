using Jellyfin.Plugin.HwProbe.Core.Speed;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Describes a library item's file for a speed run, from what Jellyfin already knows about its streams.</summary>
/// <param name="library">The library.</param>
/// <param name="mediaSources">The media sources.</param>
public sealed class LibraryFiles(ILibraryManager library, IMediaSourceManager mediaSources)
{
    /// <summary>Finds an item's file.</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>The file, or null when the item doesn't exist or isn't a local video file.</returns>
    public SpeedFile? Find(Guid itemId)
    {
        // GetStaticMediaSources throws for an item without media sources, such as a series.
        if (library.GetItemById(itemId) is not { } item || item is not MediaBrowser.Controller.Entities.IHasMediaSources)
        {
            return null;
        }

        var source = mediaSources.GetStaticMediaSources(item, false, null).FirstOrDefault(s => s.Protocol == MediaProtocol.File && !string.IsNullOrEmpty(s.Path));
        return source is null ? null : Describe(item.Name, source);
    }

    /// <summary>Describes one media source.</summary>
    /// <param name="name">The item's name.</param>
    /// <param name="source">The media source.</param>
    /// <returns>The file, or null when it has no video stream.</returns>
    internal static SpeedFile? Describe(string name, MediaSourceInfo source)
    {
        var streams = source.MediaStreams ?? [];
        if (streams.FirstOrDefault(s => s.Type == MediaStreamType.Video) is not { } video || video.Width is not { } width || video.Height is not { } height)
        {
            return null;
        }

        // The real frame rate can read double (47.952 for 23.976 content), which would halve the streams counted.
        var audio = streams.FirstOrDefault(s => s.Type == MediaStreamType.Audio && s.Index == source.DefaultAudioStreamIndex)
            ?? streams.FirstOrDefault(s => s.Type == MediaStreamType.Audio);
        return new SpeedFile(source.Path, name, TimeSpan.FromTicks(source.RunTimeTicks ?? 0), new SpeedFileVideo(video.Index, video.Codec, video.BitDepth ?? 8, width, height, video.AverageFrameRate ?? video.RealFrameRate ?? 24)
        {
            Profile = video.Profile,
            PixelFormat = video.PixelFormat,
            Interlaced = video.IsInterlaced,
            ColorTransfer = video.ColorTransfer,
            ColorPrimaries = video.ColorPrimaries,
            ColorSpace = video.ColorSpace,
            AspectRatio = video.AspectRatio,
        })
        {
            MediaSourceId = source.Id,
            Audio = audio is null ? null : new SpeedFileAudio(audio.Index, audio.Codec, audio.Channels ?? 2),
            Subtitles = [.. streams.Where(s => s.Type == MediaStreamType.Subtitle).Select(s => new SpeedFileSubtitle(s.Index, s.Codec, s.IsTextSubtitleStream, s.Language ?? s.Title) { ExternalPath = s.IsExternal ? s.Path : null })],
        };
    }
}
