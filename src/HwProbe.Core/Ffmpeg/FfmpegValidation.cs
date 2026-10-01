namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Whether an ffmpeg build passes upstream's version gate.</summary>
public enum FfmpegValidation
{
    /// <summary>Version is at least <see cref="FfmpegVersionParser.MinVersion"/>.</summary>
    Valid,

    /// <summary><c>-version</c> produced no output.</summary>
    NoOutput,

    /// <summary>The binary is Libav's avconv, which upstream rejects.</summary>
    Libav,

    /// <summary>Neither the banner nor the library versions identify a supported version.</summary>
    UnknownVersion,

    /// <summary>The version is below <see cref="FfmpegVersionParser.MinVersion"/>.</summary>
    TooOld,
}
