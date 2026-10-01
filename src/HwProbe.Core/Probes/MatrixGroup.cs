namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Which report column a probe cell fills.</summary>
public enum MatrixGroup
{
    /// <summary>The smoke probe; not a report column.</summary>
    Smoke,

    /// <summary>Hardware decode of the fixture's codec.</summary>
    Decode,

    /// <summary>Hardware encode to the cell's output codec.</summary>
    Encode,

    /// <summary>Hardware HDR-to-SDR tone-map.</summary>
    Tonemap,

    /// <summary>Hardware deinterlacing.</summary>
    Deinterlace,
}
