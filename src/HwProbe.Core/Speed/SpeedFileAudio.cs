namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>A file's audio stream.</summary>
/// <param name="Index">The stream index in the file.</param>
/// <param name="Codec">The codec, e.g. <c>eac3</c>.</param>
/// <param name="Channels">The channel count.</param>
public sealed record SpeedFileAudio(int Index, string Codec, int Channels);
