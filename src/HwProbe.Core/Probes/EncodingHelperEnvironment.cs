namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Process environment variables that EncodingHelper sets while generating arguments.</summary>
public static class EncodingHelperEnvironment
{
    /// <summary>Gets every variable <c>GetInputVideoHwaccelArgs</c> sets (EncodingHelper.cs, v12.1, L1058-1077).</summary>
    public static IReadOnlyList<string> Variables { get; } = ["LIBVA_DRIVER_NAME", "LIBVA_DRIVER_NAME_JELLYFIN", "AMD_DEBUG"];
}
