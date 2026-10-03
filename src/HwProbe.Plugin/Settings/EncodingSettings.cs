using System.Globalization;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HwProbe.Settings;

/// <summary>Reads and writes the <see cref="EncodingOptions"/> values HwProbe may change, by setting key.</summary>
/// <remarks>Keys match <c>SettingAdvice.Setting</c>; values are strings so history entries can hold any of them.</remarks>
public static class EncodingSettings
{
    /// <summary>Prefix of a key naming one codec in <see cref="EncodingOptions.HardwareDecodingCodecs"/>.</summary>
    public const string CodecPrefix = "HardwareDecodingCodecs:";

    /// <summary>Key for the deinterlacing method as a flag: <c>true</c> is BWDIF, <c>false</c> YADIF.</summary>
    public const string Bwdif = "DeinterlaceMethod:bwdif";

    private static readonly Dictionary<string, (Func<EncodingOptions, bool> Get, Action<EncodingOptions, bool> Set)> _flags = new(StringComparer.Ordinal)
    {
        [nameof(EncodingOptions.EnableDecodingColorDepth10Hevc)] = (o => o.EnableDecodingColorDepth10Hevc, (o, v) => o.EnableDecodingColorDepth10Hevc = v),
        [nameof(EncodingOptions.EnableDecodingColorDepth10Vp9)] = (o => o.EnableDecodingColorDepth10Vp9, (o, v) => o.EnableDecodingColorDepth10Vp9 = v),
        [nameof(EncodingOptions.EnableDecodingColorDepth10HevcRext)] = (o => o.EnableDecodingColorDepth10HevcRext, (o, v) => o.EnableDecodingColorDepth10HevcRext = v),
        [nameof(EncodingOptions.EnableDecodingColorDepth12HevcRext)] = (o => o.EnableDecodingColorDepth12HevcRext, (o, v) => o.EnableDecodingColorDepth12HevcRext = v),
        [nameof(EncodingOptions.PreferSystemNativeHwDecoder)] = (o => o.PreferSystemNativeHwDecoder, (o, v) => o.PreferSystemNativeHwDecoder = v),
        [nameof(EncodingOptions.EnableEnhancedNvdecDecoder)] = (o => o.EnableEnhancedNvdecDecoder, (o, v) => o.EnableEnhancedNvdecDecoder = v),
        [Bwdif] = (o => o.DeinterlaceMethod == DeinterlaceMethod.bwdif, (o, v) => o.DeinterlaceMethod = v ? DeinterlaceMethod.bwdif : DeinterlaceMethod.yadif),
        [nameof(EncodingOptions.EnableHardwareEncoding)] = (o => o.EnableHardwareEncoding, (o, v) => o.EnableHardwareEncoding = v),
        [nameof(EncodingOptions.EnableIntelLowPowerH264HwEncoder)] = (o => o.EnableIntelLowPowerH264HwEncoder, (o, v) => o.EnableIntelLowPowerH264HwEncoder = v),
        [nameof(EncodingOptions.EnableIntelLowPowerHevcHwEncoder)] = (o => o.EnableIntelLowPowerHevcHwEncoder, (o, v) => o.EnableIntelLowPowerHevcHwEncoder = v),
        [nameof(EncodingOptions.AllowHevcEncoding)] = (o => o.AllowHevcEncoding, (o, v) => o.AllowHevcEncoding = v),
        [nameof(EncodingOptions.AllowAv1Encoding)] = (o => o.AllowAv1Encoding, (o, v) => o.AllowAv1Encoding = v),
        [nameof(EncodingOptions.EnableTonemapping)] = (o => o.EnableTonemapping, (o, v) => o.EnableTonemapping = v),
        [nameof(EncodingOptions.EnableVppTonemapping)] = (o => o.EnableVppTonemapping, (o, v) => o.EnableVppTonemapping = v),
        [nameof(EncodingOptions.EnableVideoToolboxTonemapping)] = (o => o.EnableVideoToolboxTonemapping, (o, v) => o.EnableVideoToolboxTonemapping = v),
        [nameof(EncodingOptions.EnableAudioVbr)] = (o => o.EnableAudioVbr, (o, v) => o.EnableAudioVbr = v),
        [nameof(EncodingOptions.DeinterlaceDoubleRate)] = (o => o.DeinterlaceDoubleRate, (o, v) => o.DeinterlaceDoubleRate = v),
    };

    // Values a performance test can suggest, kept as strings in the forms the enums and invariant numbers print.
    private static readonly Dictionary<string, (Func<EncodingOptions, string> Get, Action<EncodingOptions, string> Set)> _values = new(StringComparer.Ordinal)
    {
        [nameof(EncodingOptions.EncoderPreset)] = (o => o.EncoderPreset.ToString(), (o, v) => o.EncoderPreset = Enum.Parse<EncoderPreset>(v)),
        [nameof(EncodingOptions.EncodingThreadCount)] = (o => Whole(o.EncodingThreadCount), (o, v) => o.EncodingThreadCount = int.Parse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
        [nameof(EncodingOptions.H264Crf)] = (o => Whole(o.H264Crf), (o, v) => o.H264Crf = int.Parse(v, NumberStyles.None, CultureInfo.InvariantCulture)),
        [nameof(EncodingOptions.H265Crf)] = (o => Whole(o.H265Crf), (o, v) => o.H265Crf = int.Parse(v, NumberStyles.None, CultureInfo.InvariantCulture)),
        [nameof(EncodingOptions.TonemappingAlgorithm)] = (o => o.TonemappingAlgorithm.ToString(), (o, v) => o.TonemappingAlgorithm = Enum.Parse<TonemappingAlgorithm>(v)),
        [nameof(EncodingOptions.TonemappingMode)] = (o => o.TonemappingMode.ToString(), (o, v) => o.TonemappingMode = Enum.Parse<TonemappingMode>(v)),
        [nameof(EncodingOptions.TonemappingRange)] = (o => o.TonemappingRange.ToString(), (o, v) => o.TonemappingRange = Enum.Parse<TonemappingRange>(v)),
        [nameof(EncodingOptions.TonemappingDesat)] = (o => Real(o.TonemappingDesat), (o, v) => o.TonemappingDesat = ParseReal(v)),
        [nameof(EncodingOptions.TonemappingPeak)] = (o => Real(o.TonemappingPeak), (o, v) => o.TonemappingPeak = ParseReal(v)),
        [nameof(EncodingOptions.TonemappingParam)] = (o => Real(o.TonemappingParam), (o, v) => o.TonemappingParam = ParseReal(v)),
        [nameof(EncodingOptions.DownMixStereoAlgorithm)] = (o => o.DownMixStereoAlgorithm.ToString(), (o, v) => o.DownMixStereoAlgorithm = Enum.Parse<DownMixStereoAlgorithms>(v)),
        [nameof(EncodingOptions.DownMixAudioBoost)] = (o => Real(o.DownMixAudioBoost), (o, v) => o.DownMixAudioBoost = ParseReal(v)),
    };

    /// <summary>Reports whether a key names a value HwProbe may change.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns>True for a known key.</returns>
    public static bool IsKnown(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _flags.ContainsKey(key)
            || _values.ContainsKey(key)
            || (key.StartsWith(CodecPrefix, StringComparison.Ordinal) && key.Length > CodecPrefix.Length)
            || key is nameof(EncodingOptions.HardwareAccelerationType) or nameof(EncodingOptions.VaapiDevice) or nameof(EncodingOptions.QsvDevice);
    }

    /// <summary>Reads a value.</summary>
    /// <param name="options">The options.</param>
    /// <param name="key">A known setting key.</param>
    /// <returns><c>true</c>/<c>false</c> for flags and codecs, the backend's lowercase name, or the device path.</returns>
    public static string Read(EncodingOptions options, string key)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(key);
        if (_flags.TryGetValue(key, out var flag))
        {
            return Format(flag.Get(options));
        }

        if (_values.TryGetValue(key, out var known))
        {
            return known.Get(options);
        }

        if (key.StartsWith(CodecPrefix, StringComparison.Ordinal))
        {
            return Format((options.HardwareDecodingCodecs ?? []).Contains(key[CodecPrefix.Length..], StringComparer.Ordinal));
        }

        return key switch
        {
            nameof(EncodingOptions.HardwareAccelerationType) => options.HardwareAccelerationType.ToString(),
            nameof(EncodingOptions.VaapiDevice) => options.VaapiDevice ?? string.Empty,
            nameof(EncodingOptions.QsvDevice) => options.QsvDevice ?? string.Empty,
            _ => throw new ArgumentException($"Unknown setting {key}.", nameof(key)),
        };
    }

    /// <summary>Writes a value.</summary>
    /// <param name="options">The options to change.</param>
    /// <param name="key">A known setting key.</param>
    /// <param name="value">A value in the form <see cref="Read"/> returns.</param>
    public static void Write(EncodingOptions options, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (_flags.TryGetValue(key, out var flag))
        {
            flag.Set(options, bool.Parse(value));
            return;
        }

        if (_values.TryGetValue(key, out var known))
        {
            known.Set(options, value);
            return;
        }

        if (key.StartsWith(CodecPrefix, StringComparison.Ordinal))
        {
            var codec = key[CodecPrefix.Length..];
            var codecs = (options.HardwareDecodingCodecs ?? []).Where(c => c != codec);
            options.HardwareDecodingCodecs = [.. bool.Parse(value) ? codecs.Append(codec) : codecs];
            return;
        }

        switch (key)
        {
            case nameof(EncodingOptions.HardwareAccelerationType):
                options.HardwareAccelerationType = Enum.Parse<HardwareAccelerationType>(value);
                break;
            case nameof(EncodingOptions.VaapiDevice):
                options.VaapiDevice = value;
                break;
            case nameof(EncodingOptions.QsvDevice):
                options.QsvDevice = value;
                break;
            default:
                throw new ArgumentException($"Unknown setting {key}.", nameof(key));
        }
    }

    /// <summary>Formats a flag the way history entries store it.</summary>
    /// <param name="value">The flag.</param>
    /// <returns><c>true</c> or <c>false</c>.</returns>
    public static string Format(bool value) => value ? "true" : "false";

    /// <summary>Formats a whole number invariantly.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a number invariantly.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    private static string Real(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses an invariant number.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The number.</returns>
    private static double ParseReal(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
