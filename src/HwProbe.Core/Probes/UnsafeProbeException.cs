namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Running this probe would change process state that something else depends on.</summary>
public sealed class UnsafeProbeException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UnsafeProbeException"/> class.</summary>
    public UnsafeProbeException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsafeProbeException"/> class.</summary>
    /// <param name="message">What would change, and how to probe safely instead.</param>
    public UnsafeProbeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsafeProbeException"/> class.</summary>
    /// <param name="message">What would change.</param>
    /// <param name="innerException">The underlying failure.</param>
    public UnsafeProbeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
