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

    /// <summary>Builds the arguments the server's image extraction (trickplay's) runs for a video, with hardware decoding on for a hardware backend and off for software.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="device">Render node or adapter, or null when the backend takes none.</param>
    /// <param name="cell">The video, with <see cref="ProbeCell.SourcePath"/> set.</param>
    /// <param name="job">The image settings.</param>
    /// <returns>The arguments: <see cref="ProbeArguments.InputArgument"/> holds the whole input, <see cref="ProbeArguments.EncoderArgs"/> what follows the encoder.</returns>
    /// <exception cref="ArgumentConstructionException">The server would make them in software with this backend.</exception>
    ProbeArguments BuildImages(HwType type, string? device, ProbeCell cell, ImageJob job);
}
