using System.Globalization;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Builds the probe cell for a speed test at the run's settings.</summary>
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
            Tonemap = cell.Tonemap && settings.Tonemap,
            VppTonemap = cell.Tonemap && settings.Tonemap && settings.VppTonemap,
            EncoderPreset = settings.EncoderPreset,
            H264Crf = settings.H264Crf,
            H265Crf = settings.H265Crf,
            Audio = cell.Audio && !test.DecodeOnly,
            AudioVbr = settings.AudioVbr,
            AudioCopy = settings.AudioCopy,
            SubtitlePath = !test.DecodeOnly && settings.BurnIn == "text" ? clips.GetValueOrDefault(SpeedCatalog.TextSubtitles.FileName) : null,
            GraphicalSubtitlePath = !test.DecodeOnly && settings.BurnIn == "image" ? clips.GetValueOrDefault(SpeedCatalog.ImageSubtitles.FileName) : null,
            DoubleRate = settings.DoubleRate,
            Bwdif = settings.Bwdif,
            EncodingThreadCount = settings.EncodingThreadCount,
            PreferNativeDecoder = settings.PreferNativeDecoder,
            EnhancedNvdec = settings.EnhancedNvdec,

            // EncodingHelper.IsVideoToolboxTonemapAvailable doesn't check EnableTonemapping (v12.1).
            VideoToolboxTonemap = cell.Tonemap && settings.VideoToolboxTonemap,
            TonemapAlgorithm = settings.TonemapAlgorithm,
            TonemapMode = settings.TonemapMode,
            TonemapRange = settings.TonemapRange,
            TonemapDesat = settings.TonemapDesat,
            TonemapPeak = settings.TonemapPeak,
            TonemapParam = settings.TonemapParam,
            DownmixAlgorithm = settings.DownmixAlgorithm,
            DownmixBoost = settings.DownmixBoost,
            LowPower = output switch
            {
                "h264" => settings.LowPowerH264,
                "hevc" => settings.LowPowerHevc,
                _ => false,
            },
        };
    }

    /// <summary>Returns a test's cell for one backend: QSV takes the run's own low-power choice.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="test">The test.</param>
    /// <param name="cell">The base cell.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>The cell.</returns>
    public static ProbeCell ForBackend(HwType type, SpeedTest test, ProbeCell cell, SpeedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(settings);
        var lowPower = test.OutputCodec switch
        {
            "h264" => settings.QsvLowPowerH264,
            "hevc" => settings.QsvLowPowerHevc,
            _ => null,
        };
        return type == HwType.qsv && lowPower is { } on ? cell with { LowPower = on } : cell;
    }

    /// <summary>Returns every clip a set of tests needs.</summary>
    /// <param name="tests">The tests.</param>
    /// <param name="settings">The settings, for the subtitles burned in.</param>
    /// <returns>The clips, each once.</returns>
    public static IReadOnlyList<FixtureSpec> Clips(IEnumerable<SpeedTest> tests, SpeedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var list = tests.ToList();
        var all = list.Select(t => t.Fixture).ToList();

        // Clips that copy another are made after it.
        if (all.Any(f => f?.EncodeArguments.Contains("{clip:", StringComparison.Ordinal) == true))
        {
            all.Insert(0, SpeedCatalog.TestAudio);
        }

        if (list.Any(t => !t.DecodeOnly) && SubtitleClip(settings) is { } subtitles)
        {
            all.Add(subtitles);
        }

        return [.. all.OfType<FixtureSpec>().DistinctBy(f => f.FileName, StringComparer.Ordinal)];
    }

    /// <summary>Returns the subtitle file a run burns in.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The clip, or null when none is burned in.</returns>
    public static FixtureSpec? SubtitleClip(SpeedSettings settings) => settings?.BurnIn switch
    {
        "text" => SpeedCatalog.TextSubtitles,
        "image" => SpeedCatalog.ImageSubtitles,
        _ => null,
    };

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
