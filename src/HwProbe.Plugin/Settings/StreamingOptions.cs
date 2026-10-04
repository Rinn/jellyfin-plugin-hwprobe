namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>A copy of the server's streaming values HwProbe may change, from Playback > Streaming.</summary>
public sealed class StreamingOptions
{
    /// <summary>Gets or sets the Internet streaming bitrate limit in bits per second; 0 for none.</summary>
    public int RemoteClientBitrateLimit { get; set; }
}
