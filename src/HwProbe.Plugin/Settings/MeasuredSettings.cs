using Jellyfin.Plugin.HwProbe.Core.Data;
using MediaBrowser.Model.Configuration;

namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>Maps the catalog's performance test options to the <see cref="EncodingOptions"/> values they stand for, so a suggestion can be applied.</summary>
public static class MeasuredSettings
{
    private static readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal)
    {
        ["QsvLowPowerH264"] = nameof(EncodingOptions.EnableIntelLowPowerH264HwEncoder),
        ["QsvLowPowerHevc"] = nameof(EncodingOptions.EnableIntelLowPowerHevcHwEncoder),
        ["EnhancedNvdec"] = nameof(EncodingOptions.EnableEnhancedNvdecDecoder),
        ["PreferNativeDecoder"] = nameof(EncodingOptions.PreferSystemNativeHwDecoder),
        ["VppTonemap"] = nameof(EncodingOptions.EnableVppTonemapping),
        ["VideoToolboxTonemap"] = nameof(EncodingOptions.EnableVideoToolboxTonemapping),
        ["Tonemap"] = nameof(EncodingOptions.EnableTonemapping),
        ["TonemapAlgorithm"] = nameof(EncodingOptions.TonemappingAlgorithm),
        ["TonemapMode"] = nameof(EncodingOptions.TonemappingMode),
        ["TonemapRange"] = nameof(EncodingOptions.TonemappingRange),
        ["TonemapDesat"] = nameof(EncodingOptions.TonemappingDesat),
        ["TonemapPeak"] = nameof(EncodingOptions.TonemappingPeak),
        ["TonemapParam"] = nameof(EncodingOptions.TonemappingParam),
        ["EncodingThreadCount"] = nameof(EncodingOptions.EncodingThreadCount),
        ["AudioVbr"] = nameof(EncodingOptions.EnableAudioVbr),
        ["DownmixBoost"] = nameof(EncodingOptions.DownMixAudioBoost),
        ["DownmixAlgorithm"] = nameof(EncodingOptions.DownMixStereoAlgorithm),
        ["EncoderPreset"] = nameof(EncodingOptions.EncoderPreset),
        ["H265Crf"] = nameof(EncodingOptions.H265Crf),
        ["H264Crf"] = nameof(EncodingOptions.H264Crf),
        ["DeinterlaceMethod"] = EncodingSettings.Bwdif,
        ["DoubleRate"] = nameof(EncodingOptions.DeinterlaceDoubleRate),
        [Core.Speed.SpeedAdvisor.BitrateLimitKey] = StreamingSettings.RemoteClientBitrateLimit,
    };

    /// <summary>Returns the setting key and value to write for a catalog option's value.</summary>
    /// <param name="option">The catalog option key, e.g. <c>EncoderPreset</c>.</param>
    /// <param name="value">Its value as the catalog keys it.</param>
    /// <returns>The <see cref="EncodingSettings"/> key and value, or null for an option that isn't a server setting.</returns>
    public static (string Setting, string Value)? ToSetting(string option, string value)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(value);
        return _keys.TryGetValue(option, out var key)
            ? (key, option == "DeinterlaceMethod" ? EncodingSettings.Format(value == "bwdif") : value)
            : null;
    }

    /// <summary>Returns the catalog's label for a setting key, for history entries.</summary>
    /// <param name="setting">An <see cref="EncodingSettings"/> key.</param>
    /// <returns>The label, or null when no option writes that key.</returns>
    public static string? LabelFor(string setting)
    {
        var option = _keys.FirstOrDefault(k => k.Value == setting).Key;
        return option is null ? null : Catalog.Default.Options.FirstOrDefault(o => o.Key == option)?.Label ?? Catalog.Default.Labels.GetValueOrDefault(option);
    }
}
