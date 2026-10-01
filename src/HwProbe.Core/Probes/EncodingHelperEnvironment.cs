using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Process environment variables that EncodingHelper sets while generating arguments.</summary>
public static class EncodingHelperEnvironment
{
    /// <summary>Gets every variable <c>GetInputVideoHwaccelArgs</c> sets (EncodingHelper.cs, v12.1, L1058-1077).</summary>
    public static IReadOnlyList<string> Variables { get; } = ["LIBVA_DRIVER_NAME", "LIBVA_DRIVER_NAME_JELLYFIN", "AMD_DEBUG"];

    /// <summary>Reads the current values of <see cref="Variables"/>.</summary>
    /// <returns>Values by name; null when unset.</returns>
    public static IReadOnlyDictionary<string, string?> Capture() =>
        Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable, StringComparer.Ordinal);

    /// <summary>Returns the variables generating args will set, and their values.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="intelIhd">Whether the device reports the Intel iHD driver.</param>
    /// <param name="intelI965">Whether the device reports the Intel i965 driver.</param>
    /// <param name="amd">Whether the device reports the AMD Mesa driver.</param>
    /// <returns>Variables and values; empty when nothing is set.</returns>
    /// <remarks>
    /// Mirrors the VAAPI branch of GetInputVideoHwaccelArgs (v12.1, L1051-1077): iHD wins over i965, and
    /// AMD only applies when neither Intel driver matched. May over-predict; it must never under-predict,
    /// and ArgumentSource fails if it does.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Predict(HwType type, bool intelIhd, bool intelI965, bool amd)
    {
        Dictionary<string, string> effects = [];
        if (type != HwType.vaapi || intelIhd)
        {
            return effects;
        }

        if (intelI965)
        {
            effects["LIBVA_DRIVER_NAME"] = "i965";
            effects["LIBVA_DRIVER_NAME_JELLYFIN"] = "i965";
        }
        else if (amd)
        {
            effects["AMD_DEBUG"] = "noefc";
        }

        return effects;
    }
}
