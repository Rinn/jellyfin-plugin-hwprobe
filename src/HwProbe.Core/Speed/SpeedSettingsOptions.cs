using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Applies the catalog's run options to the settings a speed run starts from.</summary>
public static class SpeedSettingsOptions
{
    /// <summary>Returns the settings with one option set.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">The option's key, e.g. <c>EncoderPreset</c>.</param>
    /// <param name="value">Its value, already checked against the catalog.</param>
    /// <returns>The settings, or null for a key this doesn't know.</returns>
    public static SpeedSettings? Apply(SpeedSettings settings, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var on = value == "true";
        int Number() => int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        double Real() => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return key switch
        {
            "QsvLowPowerH264" => settings with { QsvLowPowerH264 = on },
            "QsvLowPowerHevc" => settings with { QsvLowPowerHevc = on },
            "EnhancedNvdec" => settings with { EnhancedNvdec = on },
            "PreferNativeDecoder" => settings with { PreferNativeDecoder = on },
            "VppTonemap" => settings with { VppTonemap = on },
            "VideoToolboxTonemap" => settings with { VideoToolboxTonemap = on },
            "Tonemap" => settings with { Tonemap = on },
            "TonemapAlgorithm" => settings with { TonemapAlgorithm = value },
            "TonemapMode" => settings with { TonemapMode = value },
            "TonemapRange" => settings with { TonemapRange = value },
            "TonemapDesat" => settings with { TonemapDesat = Real() },
            "TonemapPeak" => settings with { TonemapPeak = Real() },
            "TonemapParam" => settings with { TonemapParam = Real() },
            "EncodingThreadCount" => settings with { EncodingThreadCount = Number() },
            "AudioVbr" => settings with { AudioVbr = on },
            "DownmixBoost" => settings with { DownmixBoost = Real() },
            "DownmixAlgorithm" => settings with { DownmixAlgorithm = value },
            "EncoderPreset" => settings with { EncoderPreset = value == "auto" ? null : value },
            "H265Crf" => settings with { H265Crf = Number() },
            "H264Crf" => settings with { H264Crf = Number() },
            "DeinterlaceMethod" => settings with { Bwdif = value == "bwdif" },
            "DoubleRate" => settings with { DoubleRate = on },
            "Audio" => settings with { AudioCopy = value == "copy" },
            "BurnIn" => settings with { BurnIn = value },
            _ => null,
        };
    }
}
