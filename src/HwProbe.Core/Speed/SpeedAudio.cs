using Jellyfin.Plugin.HwProbe.Core.Fixtures;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>An audio input a speed run reads: a clip made on the server or downloaded.</summary>
/// <param name="Key">Stable key, e.g. <c>flac</c>.</param>
/// <param name="Name">What the page calls it, e.g. <c>FLAC</c>.</param>
/// <param name="Fixture">The clip.</param>
/// <param name="Codec">The codec as Jellyfin reports it.</param>
/// <param name="Channels">The channel count.</param>
/// <param name="SampleRate">The sample rate.</param>
public sealed record SpeedAudio(string Key, string Name, FixtureSpec Fixture, string Codec, int Channels, int SampleRate)
{
    /// <summary>Gets what the page adds to the name, e.g. <c>16-bit</c>.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether it's an old format, listed last.</summary>
    public bool Legacy { get; init; }

    /// <summary>Gets where it comes from, e.g. <c>Generated, 10 s</c> or <c>Downloaded, 0.3 MB</c>.</summary>
    public string Origin { get; init; } = string.Empty;
}
