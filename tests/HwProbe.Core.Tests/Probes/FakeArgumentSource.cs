using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Returns VideoToolbox-shaped arguments (software ones for none), or a construction error for chosen codecs.</summary>
internal sealed class FakeArgumentSource : IArgumentSource, IArgumentSourceFactory
{
    /// <summary>Gets input codecs that raise <see cref="ArgumentConstructionException"/>.</summary>
    public HashSet<string> Unconstructible { get; } = [];

    /// <summary>Gets input codecs Jellyfin would decode in software (no hardware decoder).</summary>
    public HashSet<string> SoftwareDecoded { get; } = [];

    /// <summary>Gets or sets a value indicating whether deinterlacing happens on the CPU.</summary>
    public bool NoHardwareDeinterlace { get; set; }

    /// <summary>Gets or sets a value indicating whether the hardware decoders don't decode key frames only, so images come from software.</summary>
    public bool NoKeyFrameDecoding { get; set; }

    /// <summary>Gets environment overrides returned with every set of arguments.</summary>
    public Dictionary<string, string?> Environment { get; } = [];

    /// <inheritdoc/>
    public IArgumentSource Create(FfmpegCapabilities capabilities, DeviceTraits traits) => this;

    /// <inheritdoc/>
    public ProbeArguments Build(HwType type, string? device, ProbeCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (Unconstructible.Contains(cell.InputCodec))
        {
            throw new ArgumentConstructionException($"no {type} args for {cell.InputCodec}");
        }

        if (type == HwType.none)
        {
            return new ProbeArguments(string.Empty, cell.Bwdif ? " -vf \"bwdif=0:-1:0\"" : string.Empty, "libx264", Environment);
        }

        var input = Hwaccel(cell);
        var filters = cell.Tonemap ? " -vf \"scale_vt=color_transfer=bt709\"" : string.Empty;
        return new ProbeArguments(input, filters, $"{cell.OutputCodec}_{type}", Environment)
        {
            HardwareDecoder = cell.HardwareDecode && !SoftwareDecoded.Contains(cell.InputCodec) ? "-hwaccel videotoolbox" : null,
            HardwareEncoder = true,
            LowPowerEncoder = cell.LowPower,
            EncoderArgs = cell.LowPower ? " -low_power 1" : string.Empty,
            HardwareTonemap = cell.Tonemap,
            HardwareDeinterlacer = cell.Interlaced && !NoHardwareDeinterlace ? "videotoolbox" : null,
        };
    }

    /// <inheritdoc/>
    public ProbeArguments BuildImages(HwType type, string? device, ProbeCell cell, ImageJob job)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(job);
        if (type != HwType.none && job.KeyFramesOnly && NoKeyFrameDecoding)
        {
            throw new ArgumentConstructionException($"no key-frame decoding on {type}");
        }

        var hwaccel = type == HwType.none ? string.Empty : Hwaccel(cell);
        var threads = type == HwType.none ? $"-threads {job.Threads} " : string.Empty;
        return new ProbeArguments(hwaccel, $"-vf \"fps=fps=0.1,scale=w={job.Width}:h=-2\"", type != HwType.none && job.HwEncoding ? "mjpeg_videotoolbox" : "mjpeg", Environment)
        {
            InputArgument = $"{(job.KeyFramesOnly ? "-skip_frame nokey " : string.Empty)}{threads}{hwaccel} -i file:\"{cell.SourcePath}\" -map 0:0".Replace("  ", " ", StringComparison.Ordinal),
            EncoderArgs = $"-qscale:v {job.Qscale} -fps_mode passthrough",
            Threads = job.Threads,
            HardwareDecoder = type != HwType.none && cell.HardwareDecode && !SoftwareDecoded.Contains(cell.InputCodec) ? "-hwaccel videotoolbox" : null,
            HardwareEncoder = type != HwType.none && job.HwEncoding,
        };
    }

    /// <summary>Returns the VideoToolbox device and decode arguments for a cell.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The arguments.</returns>
    private static string Hwaccel(ProbeCell cell) => cell.HardwareDecode ? "-init_hw_device videotoolbox=vt -hwaccel videotoolbox" : "-init_hw_device videotoolbox=vt";
}
