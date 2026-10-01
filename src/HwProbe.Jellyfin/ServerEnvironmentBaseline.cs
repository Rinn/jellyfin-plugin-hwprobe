namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>EncodingHelper's environment variables as they were when the plugin loaded.</summary>
/// <param name="Values">Values by name; null when unset.</param>
public sealed record ServerEnvironmentBaseline(IReadOnlyDictionary<string, string?> Values);
