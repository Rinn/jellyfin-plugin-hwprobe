using Jellyfin.Plugin.HwProbe.Settings;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>Reading and writing encoding options by setting key.</summary>
[Trait("Category", "Unit")]
public sealed class EncodingSettingsTests
{
    /// <summary>A codec key adds and removes one entry of the decoding list.</summary>
    [Fact]
    public void CodecKeysEditTheDecodingList()
    {
        var options = new EncodingOptions { HardwareDecodingCodecs = ["h264", "vc1"] };

        EncodingSettings.Write(options, "HardwareDecodingCodecs:hevc", "true");
        EncodingSettings.Write(options, "HardwareDecodingCodecs:vc1", "false");

        Assert.Equal(["h264", "hevc"], options.HardwareDecodingCodecs);
        Assert.Equal("true", EncodingSettings.Read(options, "HardwareDecodingCodecs:hevc"));
        Assert.Equal("false", EncodingSettings.Read(options, "HardwareDecodingCodecs:vc1"));
    }

    /// <summary>Flags, the backend and devices round-trip through their string form.</summary>
    [Fact]
    public void ValuesRoundTrip()
    {
        var options = new EncodingOptions();

        EncodingSettings.Write(options, "EnableIntelLowPowerH264HwEncoder", "true");
        EncodingSettings.Write(options, "HardwareAccelerationType", "qsv");
        EncodingSettings.Write(options, "QsvDevice", "/dev/dri/renderD128");

        Assert.True(options.EnableIntelLowPowerH264HwEncoder);
        Assert.Equal(HardwareAccelerationType.qsv, options.HardwareAccelerationType);
        Assert.Equal("qsv", EncodingSettings.Read(options, "HardwareAccelerationType"));
        Assert.Equal("/dev/dri/renderD128", EncodingSettings.Read(options, "QsvDevice"));
    }

    /// <summary>Every key the advisor can produce is known; anything else is not.</summary>
    [Fact]
    public void KnowsTheAdvisorKeysOnly()
    {
        string[] advisorKeys =
        [
            "HardwareDecodingCodecs:h264", "EnableDecodingColorDepth10Hevc", "EnableDecodingColorDepth10Vp9", "EnableDecodingColorDepth10HevcRext",
            "EnableDecodingColorDepth12HevcRext", "PreferSystemNativeHwDecoder", "EnableHardwareEncoding", "EnableIntelLowPowerH264HwEncoder",
            "EnableIntelLowPowerHevcHwEncoder", "AllowHevcEncoding", "AllowAv1Encoding", "EnableTonemapping", "EnableVppTonemapping", "EnableVideoToolboxTonemapping",
        ];

        Assert.All(advisorKeys, k => Assert.True(EncodingSettings.IsKnown(k), k));
        Assert.False(EncodingSettings.IsKnown("EncoderAppPath"));
        Assert.False(EncodingSettings.IsKnown("HardwareDecodingCodecs:"));
        Assert.Throws<ArgumentException>(() => EncodingSettings.Read(new EncodingOptions(), "EncoderAppPath"));
    }
}
