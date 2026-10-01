namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>How probes treat the process environment that EncodingHelper writes to.</summary>
/// <param name="Baseline">Values at start-up; a probe's ffmpeg gets these unless generation set a variable.</param>
/// <param name="RestoreAfterGeneration">Undo generation's writes afterwards; only safe when hwprobe owns the process.</param>
/// <param name="ServerOwned">Inside the server: variables its own configuration already sets, with their values.</param>
public sealed record EnvironmentRules(
    IReadOnlyDictionary<string, string?> Baseline,
    bool RestoreAfterGeneration,
    IReadOnlyDictionary<string, string> ServerOwned)
{
    /// <summary>Rules for the CLI, which owns its process: capture now, restore after every generation.</summary>
    /// <returns>The rules.</returns>
    public static EnvironmentRules Standalone() => new(EncodingHelperEnvironment.Capture(), true, new Dictionary<string, string>());

    /// <summary>Rules inside the Jellyfin server, where its own transcodes share the environment.</summary>
    /// <param name="baseline">Values captured when the plugin loaded.</param>
    /// <param name="serverOwned">Variables the server's own configuration sets.</param>
    /// <returns>The rules: never restore, and refuse to set anything the server doesn't.</returns>
    public static EnvironmentRules InServer(IReadOnlyDictionary<string, string?> baseline, IReadOnlyDictionary<string, string> serverOwned) =>
        new(baseline, false, serverOwned);
}
