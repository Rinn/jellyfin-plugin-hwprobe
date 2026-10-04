using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.HwProbe.Core.Resources;

/// <summary>Reads NVIDIA GPU 0's engine use through NVML (libnvidia-ml), for Linux, where NVIDIA's driver has no DRM fdinfo engine stats.</summary>
/// <remarks>Whole-device figures: NVML's per-process figures are empty in containers, whose process ids NVML doesn't see (checked in Docker on WSL2 with an RTX 5080). Jellyfin uses CUDA device 0 (EncodingHelper).</remarks>
internal static unsafe class Nvml
{
    private static readonly Lazy<Functions?> _functions = new(Load);

    /// <summary>Gets a value indicating whether NVML loaded and found a GPU.</summary>
    public static bool Available => _functions.Value is not null;

    /// <summary>Returns GPU 0's current encoder, decoder, and SM use, each 0 to 1.</summary>
    /// <returns>Use by engine name, or null when NVML isn't available or the read fails.</returns>
    public static Dictionary<string, double>? Read()
    {
        if (_functions.Value is not { } f)
        {
            return null;
        }

        uint encode, decode, period;
        Utilization rates;
        if (((delegate* unmanaged<nint, uint*, uint*, int>)f.Encoder)(f.Device, &encode, &period) != 0
            || ((delegate* unmanaged<nint, uint*, uint*, int>)f.Decoder)(f.Device, &decode, &period) != 0
            || ((delegate* unmanaged<nint, Utilization*, int>)f.Rates)(f.Device, &rates) != 0)
        {
            return null;
        }

        return new Dictionary<string, double>(StringComparer.Ordinal) { ["VideoEncode"] = encode / 100.0, ["VideoDecode"] = decode / 100.0, ["3D"] = rates.Gpu / 100.0 };
    }

    /// <summary>Loads libnvidia-ml and opens GPU 0.</summary>
    /// <returns>The functions, or null without NVIDIA's driver or a GPU.</returns>
    private static Functions? Load()
    {
        try
        {
            return LoadLibrary();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>Loads libnvidia-ml and opens GPU 0, letting a broken library's exceptions through.</summary>
    /// <returns>The functions, or null without NVIDIA's driver or a GPU.</returns>
    private static Functions? LoadLibrary()
    {
        if (!OperatingSystem.IsLinux() || !NativeLibrary.TryLoad("libnvidia-ml.so.1", out var library))
        {
            return null;
        }

        nint Export(string name) => NativeLibrary.TryGetExport(library, name, out var address) ? address : 0;
        var init = (delegate* unmanaged<int>)Export("nvmlInit_v2");
        var handle = (delegate* unmanaged<uint, nint*, int>)Export("nvmlDeviceGetHandleByIndex_v2");
        var encoder = Export("nvmlDeviceGetEncoderUtilization");
        var decoder = Export("nvmlDeviceGetDecoderUtilization");
        var rates = Export("nvmlDeviceGetUtilizationRates");
        nint device;
        return init == null || handle == null || encoder == 0 || decoder == 0 || rates == 0 || init() != 0 || handle(0, &device) != 0
            ? null
            : new Functions(device, encoder, decoder, rates);
    }

    /// <summary>nvmlUtilization_t.</summary>
    /// <param name="Gpu">Percent of time a kernel ran.</param>
    /// <param name="Memory">Percent of time memory was read or written.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Utilization(uint Gpu, uint Memory);

    /// <summary>The loaded NVML functions and GPU 0's handle.</summary>
    /// <param name="Device">GPU 0.</param>
    /// <param name="Encoder">nvmlDeviceGetEncoderUtilization.</param>
    /// <param name="Decoder">nvmlDeviceGetDecoderUtilization.</param>
    /// <param name="Rates">nvmlDeviceGetUtilizationRates.</param>
    private sealed record Functions(nint Device, nint Encoder, nint Decoder, nint Rates);
}
