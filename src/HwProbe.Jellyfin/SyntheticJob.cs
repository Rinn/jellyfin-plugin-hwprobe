using Jellyfin.Plugin.HwProbe.Core.Probes;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>Builds the <see cref="EncodingJobInfo"/> EncodingHelper reads for one probe cell.</summary>
public static class SyntheticJob
{
    /// <summary>Creates a progressive video job whose source stream matches the cell.</summary>
    /// <param name="cell">The media shape to describe.</param>
    /// <param name="sourcePath">The input clip, or null when only the arguments around the input are wanted.</param>
    /// <returns>The job.</returns>
    public static EncodingJobInfo Create(ProbeCell cell, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var stream = new MediaStream
        {
            Index = cell.VideoIndex,
            Type = MediaStreamType.Video,
            Codec = cell.InputCodec,
            BitDepth = cell.BitDepth,
            Profile = cell.Profile,
            PixelFormat = cell.PixelFormat ?? (cell.BitDepth > 8 ? "yuv420p10le" : "yuv420p"),
            IsInterlaced = cell.Interlaced,
            Width = cell.SourceWidth,
            Height = cell.SourceHeight,
            AverageFrameRate = cell.SourceFrameRate,
            RealFrameRate = cell.SourceFrameRate,
            ColorTransfer = cell.ColorTransfer,
            ColorPrimaries = cell.ColorPrimaries,
            ColorSpace = cell.ColorSpace,
        };

        // External, like a track the server has extracted: it reaches the same subtitles= burn-in filter
        // without needing the server's subtitle extraction.
        var subtitle = cell.InternalSubtitleIndex is { } index
            ? new MediaStream { Index = index, Type = MediaStreamType.Subtitle, Codec = cell.InternalSubtitleCodec }
            : cell.SubtitlePath is null && cell.GraphicalSubtitlePath is null ? null : new MediaStream
            {
                Index = 2,
                Type = MediaStreamType.Subtitle,
                Codec = cell.GraphicalSubtitlePath is null ? "ass" : "PGSSUB",
                IsExternal = true,
                Path = cell.GraphicalSubtitlePath ?? cell.SubtitlePath,
            };

        var audio = !cell.Audio ? null : new MediaStream
        {
            Index = cell.AudioIndex,
            Type = MediaStreamType.Audio,
            Codec = cell.AudioCodec,
            Channels = cell.AudioChannels,
            SampleRate = 48000,
        };

        var source = new MediaSourceInfo
        {
            Id = cell.MediaSourceId ?? "hwprobe",
            Protocol = MediaProtocol.File,
            VideoType = VideoType.VideoFile,
            Path = sourcePath,
            MediaStreams = Streams(stream, audio, subtitle),
        };

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            SubtitleStream = subtitle,
            SubtitleDeliveryMethod = subtitle is null ? SubtitleDeliveryMethod.External : SubtitleDeliveryMethod.Encode,
            IsVideoRequest = true,
            IsInputVideo = true,
            VideoType = VideoType.VideoFile,
            VideoStream = stream,
            MediaSource = source,
            OutputVideoCodec = cell.OutputCodec,
            MediaPath = sourcePath,
            AudioStream = audio,
            OutputAudioCodec = audio is null ? null : cell.AudioCopy ? "copy" : "aac",
            OutputAudioChannels = audio is null || cell.AudioCopy ? null : 2,
            BaseRequest = new BaseEncodingJobOptions { MaxWidth = cell.MaxWidth, MaxHeight = cell.MaxHeight, VideoBitRate = cell.VideoBitrate, EnableAudioVbrEncoding = true },
        };
    }

    /// <summary>Lists the streams in index order, with a placeholder for every index in between.</summary>
    /// <param name="video">The video stream.</param>
    /// <param name="audio">The audio stream, or null.</param>
    /// <param name="subtitle">The subtitle stream, or null.</param>
    /// <returns>The list.</returns>
    /// <remarks>
    /// EncodingHelper.FindIndex (v12.1) maps a stream to ffmpeg's <c>[0:N]</c> by its position among streams with the same
    /// path, not by its index, so a real file's other tracks must be there for a subtitle at index 3 to be <c>[0:3]</c>.
    /// </remarks>
    private static List<MediaStream> Streams(MediaStream video, MediaStream? audio, MediaStream? subtitle)
    {
        List<MediaStream> known = [.. new[] { video, audio, subtitle }.OfType<MediaStream>()];
        var inside = known.Where(s => !s.IsExternal).ToList();
        var last = inside.Max(s => s.Index);
        List<MediaStream> streams = [.. Enumerable.Range(0, last + 1).Select(i => inside.Find(s => s.Index == i) ?? new MediaStream { Index = i, Type = MediaStreamType.Data })];
        streams.AddRange(known.Where(s => s.IsExternal));
        return streams;
    }
}
