using System.Reflection;
using System.Runtime.ExceptionServices;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>Forwards to the server's IMediaEncoder, but reports VAAPI driver traits for the probed device.</summary>
/// <remarks>
/// The server's encoder answers for its configured device only. Public and unsealed because
/// <see cref="DispatchProxy"/> generates a subclass at runtime.
/// </remarks>
public class TraitMediaEncoder : DispatchProxy
{
    private IMediaEncoder? _inner;
    private DeviceTraits _traits = new(VaapiDriver.Other);

    /// <summary>Wraps a media encoder.</summary>
    /// <param name="inner">The server's media encoder.</param>
    /// <param name="traits">Traits found when opening the probed device.</param>
    /// <returns>The wrapped encoder.</returns>
    public static IMediaEncoder Create(IMediaEncoder inner, DeviceTraits traits)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(traits);

        var proxy = Create<IMediaEncoder, TraitMediaEncoder>();
        var wrapper = (TraitMediaEncoder)(object)proxy;
        wrapper._inner = inner;
        wrapper._traits = traits;
        return proxy;
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        switch (targetMethod.Name)
        {
            case "get_IsVaapiDeviceInteliHD":
                return _traits.Driver == VaapiDriver.IntelIhd;
            case "get_IsVaapiDeviceInteli965":
                return _traits.Driver == VaapiDriver.IntelI965;
            case "get_IsVaapiDeviceAmd":
                return _traits.Driver == VaapiDriver.Amd;
            default:
                try
                {
                    return targetMethod.Invoke(_inner, args);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
        }
    }
}
