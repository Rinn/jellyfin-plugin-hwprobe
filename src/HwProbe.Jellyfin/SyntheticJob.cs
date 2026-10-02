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
            Index = 0,
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
        var subtitle = cell.SubtitlePath is null && cell.GraphicalSubtitlePath is null ? null : new MediaStream
        {
            Index = 2,
            Type = MediaStreamType.Subtitle,
            Codec = cell.GraphicalSubtitlePath is null ? "ass" : "PGSSUB",
            IsExternal = true,
            Path = cell.GraphicalSubtitlePath ?? cell.SubtitlePath,
        };

        var audio = !cell.Audio ? null : new MediaStream
        {
            Index = 1,
            Type = MediaStreamType.Audio,
            Codec = "aac",
            Channels = 6,
            ChannelLayout = "5.1",
            SampleRate = 48000,
        };

        var source = new MediaSourceInfo
        {
            Id = "hwprobe",
            Protocol = MediaProtocol.File,
            VideoType = VideoType.VideoFile,
            Path = sourcePath,
            MediaStreams = [.. new[] { stream, audio, subtitle }.OfType<MediaStream>()],
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
            OutputAudioCodec = audio is null ? null : "aac",
            OutputAudioChannels = audio is null ? null : 2,
            BaseRequest = new BaseEncodingJobOptions { MaxWidth = cell.MaxWidth, MaxHeight = cell.MaxHeight, VideoBitRate = cell.VideoBitrate, EnableAudioVbrEncoding = true },
        };
    }
}
