namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>A speed run as the plugin page asks for it.</summary>
/// <param name="Method">quick, confirm or full.</param>
/// <param name="Tests">Test keys; empty for the default.</param>
/// <param name="Comparisons">Comparison names: AudioVbr, Preset, Quality, Bitrate, Deinterlace, Paths.</param>
public sealed record SpeedRequest(string Method, IReadOnlyList<string> Tests, IReadOnlyList<string> Comparisons);
