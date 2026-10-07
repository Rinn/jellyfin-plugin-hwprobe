using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Generates the ffmpeg arguments upstream Jellyfin would use for a probe.</summary>
/// <remarks>
/// Implementations may mutate process environment while generating (EncodingHelper does), so callers
/// must hold the probe lock across generate, launch and restore.
/// </remarks>
public interface IArgumentSource
{
    /// <summary>Builds the arguments for one probe.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="device">Render node or adapter, or null when the backend takes none.</param>
    /// <param name="cell">The media shape to probe.</param>
    /// <returns>The generated arguments.</returns>
    /// <exception cref="ArgumentConstructionException">Upstream emits no hardware arguments for this combination.</exception>
    ProbeArguments Build(HwType type, string? device, ProbeCell cell);

    /// <summary>Builds the arguments the server runs to transcode an audio file for a client, as it does for music; always in software.</summary>
    /// <param name="cell">The file and what the client asks for.</param>
    /// <returns>The arguments; decode-only cells get the input alone.</returns>
    AudioArguments BuildAudio(AudioCell cell);
}
