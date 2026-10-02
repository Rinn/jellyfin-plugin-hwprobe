using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Builds the probe cell for a speed test at the base settings, and one per comparison.</summary>
internal static class SpeedVariants
{
    /// <summary>Returns the cell for a test at the base settings.</summary>
    /// <param name="test">The test.</param>
    /// <param name="settings">The base settings.</param>
    /// <param name="clips">Clip paths by file name.</param>
    /// <returns>The cell.</returns>
    public static ProbeCell Base(SpeedTest test, SpeedSettings settings, IReadOnlyDictionary<string, string> clips)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(settings);
        var output = test.OutputCodec ?? "h264";
        var cell = test.File is { } file ? FromFile(file, test, output, settings) : FromClip(test, output, clips);
        return cell with
        {
            SourceWidth = test.Width,
            SourceHeight = test.Height,
            SourceFrameRate = test.FrameRate,
            VideoBitrate = test.DecodeOnly ? null : test.Bitrate,
            FullQuality = true,
            VppTonemap = cell.Tonemap && settings.VppTonemap,
            EncoderPreset = settings.EncoderPreset,
            H264Crf = settings.H264Crf,
            H265Crf = settings.H265Crf,
            Audio = cell.Audio && !test.DecodeOnly,
            AudioVbr = settings.AudioVbr,
            AudioCopy = settings.AudioCopy,
            DoubleRate = settings.DoubleRate,
            Bwdif = settings.Bwdif,
            EncodingThreadCount = settings.EncodingThreadCount,
            PreferNativeDecoder = settings.PreferNativeDecoder,
            EnhancedNvdec = settings.EnhancedNvdec,
            LowPower = output switch
            {
                "h264" => settings.LowPowerH264,
                "hevc" => settings.LowPowerHevc,
                _ => false,
            },
        };
    }

    /// <summary>Returns the comparison cells that apply to a backend and test.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="test">The test.</param>
    /// <param name="cell">The base cell.</param>
    /// <param name="comparisons">The comparisons asked for.</param>
    /// <param name="clips">Clip paths by file name, for burned-in subtitles; a missing clip skips its comparison.</param>
    /// <returns>Each comparison's label and cell.</returns>
    public static IEnumerable<(string Label, ProbeCell Cell)> For(HwType type, SpeedTest test, ProbeCell cell, SpeedComparison comparisons, IReadOnlyDictionary<string, string> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var transcode = !test.DecodeOnly;
        if (transcode && comparisons.HasFlag(SpeedComparison.Subtitles))
        {
            if (clips.TryGetValue(SpeedCatalog.TextSubtitles.FileName, out var text))
            {
                yield return ("Text subtitles burned in", cell with { SubtitlePath = text });
            }

            if (clips.TryGetValue(SpeedCatalog.ImageSubtitles.FileName, out var image))
            {
                yield return ("PGS subtitles burned in", cell with { GraphicalSubtitlePath = image });
            }
        }

        if (transcode && !cell.AudioCopy && comparisons.HasFlag(SpeedComparison.AudioVbr))
        {
            yield return (cell.AudioVbr ? "VBR audio off" : "VBR audio on", cell with { AudioVbr = !cell.AudioVbr });
        }

        if (transcode && test.Interlaced && comparisons.HasFlag(SpeedComparison.Deinterlace))
        {
            yield return (cell.DoubleRate ? "Single rate" : "Double rate", cell with { DoubleRate = !cell.DoubleRate });
            yield return (cell.Bwdif ? "YADIF" : "BWDIF", cell with { Bwdif = !cell.Bwdif });
        }

        if (!comparisons.HasFlag(SpeedComparison.Paths))
        {
            yield break;
        }

        var intel = type is HwType.qsv or HwType.vaapi;
        if (intel && transcode && test.OutputCodec is "h264" or "hevc")
        {
            yield return (cell.LowPower ? "Low power off" : "Low power on", cell with { LowPower = !cell.LowPower });
        }

        if (intel && test.Tonemap)
        {
            yield return (cell.VppTonemap ? "VPP tone-mapping off" : "VPP tone-mapping on", cell with { VppTonemap = !cell.VppTonemap });
        }

        if (type == HwType.qsv)
        {
            yield return (cell.PreferNativeDecoder ? "QSV decoders" : "OS native decoders", cell with { PreferNativeDecoder = !cell.PreferNativeDecoder });
        }

        if (type == HwType.nvenc)
        {
            yield return (cell.EnhancedNvdec ? "cuvid decoders" : "NVDEC decoders", cell with { EnhancedNvdec = !cell.EnhancedNvdec });
        }
    }

    /// <summary>Returns every clip a set of tests needs.</summary>
    /// <param name="tests">The tests.</param>
    /// <param name="comparisons">The comparisons asked for; subtitles need their clips.</param>
    /// <returns>The clips, each once.</returns>
    public static IReadOnlyList<FixtureSpec> Clips(IEnumerable<SpeedTest> tests, SpeedComparison comparisons)
    {
        var all = tests.Select(t => t.Fixture).ToList();

        // Clips that copy another are made after it.
        if (all.Any(f => f?.EncodeArguments.Contains("{clip:", StringComparison.Ordinal) == true))
        {
            all.Insert(0, SpeedCatalog.TestAudio);
        }

        if (comparisons.HasFlag(SpeedComparison.Subtitles))
        {
            all.Add(SpeedCatalog.TextSubtitles);
            all.Add(SpeedCatalog.ImageSubtitles);
        }

        return [.. all.OfType<FixtureSpec>().DistinctBy(f => f.FileName, StringComparer.Ordinal)];
    }

    /// <summary>Describes a generated clip.</summary>
    /// <param name="test">The test.</param>
    /// <param name="output">The output codec.</param>
    /// <param name="clips">Clip paths by file name.</param>
    /// <returns>The cell, before the settings.</returns>
    private static ProbeCell FromClip(SpeedTest test, string output, IReadOnlyDictionary<string, string> clips)
    {
        var fixture = test.Fixture!;
        var color = fixture.IsHdr10 ? ColorMetadata.Hdr10 : null;
        return new ProbeCell(fixture.Codec, fixture.BitDepth, output, HardwareDecode: true, HardwareEncode: true)
        {
            Profile = fixture.Profile,
            PixelFormat = fixture.PixelFormat,
            Interlaced = fixture.Interlaced,
            ColorPrimaries = color?.Primaries,
            ColorTransfer = color?.Transfer,
            ColorSpace = color?.Space,
            Tonemap = test.Tonemap,
            Audio = !test.DecodeOnly,
            AudioCodec = fixture.AudioCodec ?? "aac",
            AudioChannels = fixture.AudioChannels ?? 6,
            SourcePath = clips[fixture.FileName],
        };
    }

    /// <summary>Describes a real file with its own streams.</summary>
    /// <param name="file">The file.</param>
    /// <param name="test">The test.</param>
    /// <param name="output">The output codec.</param>
    /// <param name="settings">The settings, for whether HDR is tone-mapped.</param>
    /// <returns>The cell, before the other settings.</returns>
    private static ProbeCell FromFile(SpeedFile file, SpeedTest test, string output, SpeedSettings settings)
    {
        var video = file.Video;
        return new ProbeCell(video.Codec, video.BitDepth, output, HardwareDecode: true, HardwareEncode: true)
        {
            Profile = video.Profile,
            PixelFormat = video.PixelFormat,
            Interlaced = video.Interlaced,
            ColorPrimaries = video.ColorPrimaries,
            ColorTransfer = video.ColorTransfer,
            ColorSpace = video.ColorSpace,
            Tonemap = test.Tonemap && settings.Tonemap,
            VideoIndex = video.Index,
            Audio = file.Audio is not null,
            AudioIndex = file.Audio?.Index ?? 1,
            AudioCodec = file.Audio?.Codec ?? "aac",
            AudioChannels = file.Audio?.Channels ?? 2,
            SourcePath = file.Path,
            MediaSourceId = file.MediaSourceId,
        };
    }
}
