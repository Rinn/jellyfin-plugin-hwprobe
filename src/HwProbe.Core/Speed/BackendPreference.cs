using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Which of two backends on the same GPU performance tests run and suggestions favour.</summary>
/// <remarks>
/// Jellyfin's Intel guide lists QSV on Linux as "preferred on mainstream GPUs, for better performance", with VA-API for pre-Broadwell GPUs, where QSV doesn't work
/// (jellyfin.org docs/general/post-install/transcoding/hardware-acceleration/intel.md). On a Gen 9 NAS, QSV matched VAAPI at decoding, was 7-15% faster at H.264, and was about 2.8 times as fast at HEVC.
/// </remarks>
public static class BackendPreference
{
    /// <summary>Returns the backend to measure and suggest in place of the configured one.</summary>
    /// <param name="configured">The configured backend and its device.</param>
    /// <param name="working">The backends the probe found working, with their devices.</param>
    /// <returns>QSV on the same device when VAAPI is configured and QSV works there; otherwise the configured backend.</returns>
    public static (HwType Type, string Device) Prefer((HwType Type, string Device) configured, IEnumerable<(HwType Type, string Device)> working)
    {
        ArgumentNullException.ThrowIfNull(working);
        return configured.Type == HwType.vaapi && working.Any(w => w.Type == HwType.qsv && SameDevice(w.Device, configured.Device)) ? (HwType.qsv, configured.Device) : configured;
    }

    /// <summary>Reports whether one backend is favoured over another when they measure alike.</summary>
    /// <param name="a">One result.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when <paramref name="a"/> is QSV and <paramref name="b"/> VAAPI on the same device.</returns>
    public static bool IsPreferredOver(SpeedResult a, SpeedResult b)
    {
        ArgumentNullException.ThrowIfNull(b);
        return StandsInFor(a, b.Type, b.Device);
    }

    /// <summary>Reports whether a result is from the backend favoured over another.</summary>
    /// <param name="result">The result.</param>
    /// <param name="type">The other backend.</param>
    /// <param name="device">Its device.</param>
    /// <returns>True when the result is QSV's and the other backend VAAPI on the same device.</returns>
    public static bool StandsInFor(SpeedResult result, HwType type, string device)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Type == HwType.qsv && type == HwType.vaapi && SameDevice(result.Device, device);
    }

    /// <summary>Reports whether two devices are the same GPU; an empty device matches any.</summary>
    /// <param name="a">One device.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when they match or either is empty.</returns>
    private static bool SameDevice(string a, string b) => string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b;
}
