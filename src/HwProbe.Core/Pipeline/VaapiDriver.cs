namespace Jellyfin.Plugin.HwProbe.Core.Pipeline;

/// <summary>The VA-API driver family, as upstream's <c>IsVaapiDevice*</c> flags classify it.</summary>
public enum VaapiDriver
{
    /// <summary>None of the upstream flags set.</summary>
    Other,

    /// <summary>Intel iHD (<c>IsVaapiDeviceInteliHD</c>).</summary>
    IntelIhd,

    /// <summary>Intel i965 (<c>IsVaapiDeviceInteli965</c>).</summary>
    IntelI965,

    /// <summary>AMD radeonsi (<c>IsVaapiDeviceAmd</c>).</summary>
    Amd,
}
