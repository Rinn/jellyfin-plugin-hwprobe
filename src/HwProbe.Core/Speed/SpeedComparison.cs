namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Optional runs that change one Jellyfin setting from the base, each shown beside the base result.</summary>
[Flags]
public enum SpeedComparison
{
    /// <summary>No comparisons.</summary>
    None = 0,

    /// <summary>Intel low-power H.264 and HEVC encoding the other way.</summary>
    LowPower = 32,
}
