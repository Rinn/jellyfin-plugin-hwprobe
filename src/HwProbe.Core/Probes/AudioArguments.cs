namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Arguments generated for an audio transcode, around the output the caller adds.</summary>
/// <param name="Input">The input options and every <c>-i</c>.</param>
/// <param name="Output">The output options before the output file, or empty to decode only.</param>
/// <param name="Environment">Variables set during generation, to pass to the child explicitly.</param>
public sealed record AudioArguments(string Input, string Output, IReadOnlyDictionary<string, string?> Environment);
