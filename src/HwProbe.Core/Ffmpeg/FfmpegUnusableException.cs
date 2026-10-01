namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>The ffmpeg binary is missing, too old, or Libav; nothing can be probed.</summary>
public sealed class FfmpegUnusableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="FfmpegUnusableException"/> class.</summary>
    public FfmpegUnusableException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FfmpegUnusableException"/> class.</summary>
    /// <param name="message">Why the binary can't be used.</param>
    public FfmpegUnusableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FfmpegUnusableException"/> class.</summary>
    /// <param name="message">Why the binary can't be used.</param>
    /// <param name="innerException">The underlying failure.</param>
    public FfmpegUnusableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
