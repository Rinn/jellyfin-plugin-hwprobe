namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>The argument source could not build a hardware probe for this backend and cell.</summary>
/// <remarks>Running anyway would execute a software transcode and score it as a hardware pass.</remarks>
public sealed class ArgumentConstructionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ArgumentConstructionException"/> class.</summary>
    public ArgumentConstructionException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ArgumentConstructionException"/> class.</summary>
    /// <param name="message">Why the probe cannot be built.</param>
    public ArgumentConstructionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ArgumentConstructionException"/> class.</summary>
    /// <param name="message">Why the probe cannot be built.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ArgumentConstructionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
