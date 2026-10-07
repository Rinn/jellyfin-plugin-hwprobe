namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>A setting value's slowest measured speed on one output, with the fewest concurrent streams it kept there.</summary>
/// <param name="Label">The output as the results label it, input included, which tells outputs apart.</param>
/// <param name="Video">The input video's title, e.g. Live action, or null.</param>
/// <param name="Output">The output, e.g. H.264, 8 Mbps, or null.</param>
/// <param name="Speed">The slowest measured speed, as a multiple of real time.</param>
/// <param name="Streams">The fewest concurrent streams kept, when counted.</param>
/// <param name="StreamsCapped">Whether that count hit its cap, so it's a lower bound.</param>
public sealed record OutputSpeed(string Label, string? Video, string? Output, double Speed, int? Streams, bool StreamsCapped);
