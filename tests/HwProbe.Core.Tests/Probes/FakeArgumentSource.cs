using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>Returns VideoToolbox-shaped arguments, or a construction error for chosen codecs.</summary>
internal sealed class FakeArgumentSource : IArgumentSource, IArgumentSourceFactory
{
    /// <summary>Gets input codecs that raise <see cref="ArgumentConstructionException"/>.</summary>
    public HashSet<string> Unconstructible { get; } = [];

    /// <summary>Gets input codecs Jellyfin would decode in software (no hardware decoder).</summary>
    public HashSet<string> SoftwareDecoded { get; } = [];

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

        var input = cell.HardwareDecode ? "-init_hw_device videotoolbox=vt -hwaccel videotoolbox" : "-init_hw_device videotoolbox=vt";
        var filters = cell.Tonemap ? " -vf \"scale_vt=color_transfer=bt709\"" : string.Empty;
        return new ProbeArguments(input, filters, $"{cell.OutputCodec}_{type}", Environment)
        {
            HardwareDecoder = cell.HardwareDecode && !SoftwareDecoded.Contains(cell.InputCodec) ? "-hwaccel videotoolbox" : null,
            HardwareEncoder = true,
            HardwareTonemap = cell.Tonemap,
        };
    }
}
