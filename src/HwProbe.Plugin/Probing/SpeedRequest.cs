namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>A speed run as the plugin page asks for it: every chosen output from every chosen video.</summary>
/// <param name="Method">quick, confirm or full.</param>
/// <param name="Videos">Video keys; <c>library</c> for <see cref="ItemId"/>. Empty for the default.</param>
/// <param name="Outputs">Output keys; empty for the default.</param>
/// <param name="Comparisons">Comparison names: AudioVbr, Preset, Quality, Bitrate, Deinterlace, Paths.</param>
public sealed record SpeedRequest(string Method, IReadOnlyList<string> Videos, IReadOnlyList<string> Outputs, IReadOnlyList<string> Comparisons)
{
    /// <summary>Gets how many times each measurement runs (1 to 3); more than once reports the median.</summary>
    public int Repeats { get; init; } = 1;

    /// <summary>Gets the seconds each measurement may take before it reports what it has (10 to 3600), or null for no limit.</summary>
    public int? TimeLimitSeconds { get; init; }

    /// <summary>Gets the library item the <c>library</c> video reads, or null.</summary>
    public Guid? ItemId { get; init; }
}
