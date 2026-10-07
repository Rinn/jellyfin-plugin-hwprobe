namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>One audio file and what a client asks for it, as a speed run measures it.</summary>
/// <param name="SourcePath">The audio file.</param>
/// <param name="Codec">Its codec as Jellyfin reports it.</param>
/// <param name="Channels">Its channel count.</param>
/// <param name="SampleRate">Its sample rate.</param>
public sealed record AudioCell(string SourcePath, string Codec, int Channels, int SampleRate)
{
    /// <summary>Gets the codec the client asks for, or null to decode only.</summary>
    public string? OutputCodec { get; init; }

    /// <summary>Gets the channels the client asks for.</summary>
    public int OutputChannels { get; init; } = 2;

    /// <summary>Gets a value indicating whether Jellyfin's "Enable VBR audio encoding" is on.</summary>
    public bool AudioVbr { get; init; }

    /// <summary>Gets the stereo downmix algorithm, e.g. <c>Dave750</c>.</summary>
    public string DownmixAlgorithm { get; init; } = "None";

    /// <summary>Gets the audio boost when downmixing.</summary>
    public double DownmixBoost { get; init; } = 2;

    /// <summary>Gets Jellyfin's "Transcoding thread count"; -1 is its default, automatic.</summary>
    public int EncodingThreadCount { get; init; } = -1;
}
