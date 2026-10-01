using Jellyfin.Plugin.HwProbe.Core.Probes;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>Builds the <see cref="EncodingJobInfo"/> EncodingHelper reads for one probe cell.</summary>
public static class SyntheticJob
{
    /// <summary>Fixture width; matches the generated clips.</summary>
    public const int Width = 640;

    /// <summary>Fixture height; matches the generated clips.</summary>
    public const int Height = 360;

    /// <summary>Creates a progressive video job whose source stream matches the cell.</summary>
    /// <param name="cell">The media shape to describe.</param>
    /// <returns>The job.</returns>
    public static EncodingJobInfo Create(ProbeCell cell)
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
            Width = Width,
            Height = Height,
            AverageFrameRate = 25,
            RealFrameRate = 25,
            ColorTransfer = cell.ColorTransfer,
            ColorPrimaries = cell.ColorPrimaries,
            ColorSpace = cell.ColorSpace,
        };

        var source = new MediaSourceInfo
        {
            Id = "hwprobe",
            Protocol = MediaProtocol.File,
            VideoType = VideoType.VideoFile,
            MediaStreams = [stream],
        };

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            IsVideoRequest = true,
            IsInputVideo = true,
            VideoType = VideoType.VideoFile,
            VideoStream = stream,
            MediaSource = source,
            OutputVideoCodec = cell.OutputCodec,
            BaseRequest = new BaseEncodingJobOptions { MaxWidth = cell.MaxWidth, MaxHeight = cell.MaxHeight },
        };
    }
}
