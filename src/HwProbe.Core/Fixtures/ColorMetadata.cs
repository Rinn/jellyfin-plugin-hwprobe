namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Colour tags written into a fixture and reported on its synthetic stream.</summary>
/// <param name="Primaries">ffmpeg <c>color_primaries</c> name.</param>
/// <param name="Transfer">ffmpeg <c>color_trc</c> name.</param>
/// <param name="Space">ffmpeg <c>colorspace</c> name.</param>
public sealed record ColorMetadata(string Primaries, string Transfer, string Space)
{
    /// <summary>Gets HDR10: BT.2020 primaries, PQ transfer, BT.2020 non-constant luminance.</summary>
    public static ColorMetadata Hdr10 { get; } = new("bt2020", "smpte2084", "bt2020nc");
}
