using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>One audio input, as the page lists it.</summary>
/// <param name="Key">The audio input key.</param>
/// <param name="Name">What it's called, e.g. <c>FLAC</c>.</param>
/// <param name="Input">What it is, e.g. <c>16-bit, 44.1 kHz, stereo</c>.</param>
/// <param name="Origin">Where it comes from, e.g. <c>Generated</c>.</param>
/// <param name="Legacy">Whether it's an old format, listed last.</param>
public sealed record SpeedAudioInfo(string Key, string Name, string Input, string Origin, bool Legacy)
{
    /// <summary>Returns the page's view of an audio input.</summary>
    /// <param name="audio">The audio input.</param>
    /// <returns>The view.</returns>
    public static SpeedAudioInfo From(SpeedAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return new(audio.Key, audio.Name, SpeedTestText.Input(audio), audio.Origin, audio.Legacy);
    }
}
