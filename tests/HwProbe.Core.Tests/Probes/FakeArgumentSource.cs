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

    /// <summary>Gets or sets the filter arguments tone-map cells get, or null for VideoToolbox's.</summary>
    public string? TonemapFilters { get; set; }

    /// <summary>Gets or sets the message of the <see cref="UnsafeProbeException"/> every hardware cell raises, or null for none.</summary>
    public string? Refusal { get; set; }

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

        if (Refusal is not null)
        {
            throw new UnsafeProbeException(Refusal);
        }

        var input = Hwaccel(cell);
        var filters = cell.Tonemap ? TonemapFilters ?? " -vf \"scale_vt=color_transfer=bt709\"" : string.Empty;
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
    public AudioArguments BuildAudio(AudioCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var output = cell.OutputCodec is { } codec ? $"-threads 0 -vn -ab 256000 -ac {cell.OutputChannels} -acodec {codec}" : string.Empty;
        return new AudioArguments($"-i file:\"{cell.SourcePath}\"", output, Environment);
    }

    /// <summary>Returns the VideoToolbox device and decode arguments for a cell.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The arguments.</returns>
    private static string Hwaccel(ProbeCell cell) => cell.HardwareDecode ? "-init_hw_device videotoolbox=vt -hwaccel videotoolbox" : "-init_hw_device videotoolbox=vt";
}
