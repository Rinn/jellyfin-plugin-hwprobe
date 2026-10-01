using Jellyfin.Plugin.HwProbe.Core.Storage;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Storage;

/// <summary>Stability and sensitivity of <see cref="Fingerprint"/>.</summary>
[Trait("Category", "Unit")]
public sealed class FingerprintTests
{
    private static readonly FingerprintInputs _baseline = new(
        "/usr/lib/jellyfin-ffmpeg/ffmpeg",
        "ffmpeg version 7.1.4-Jellyfin",
        ["vaapi", "qsv"],
        ["/dev/dri/renderD128"],
        new Dictionary<string, string?> { ["/dev/dri/renderD128"] = "0x8086:0x46a6:iHD" },
        null,
        "linux",
        "6.8.0");

    /// <summary>The same inputs give the same key, in the documented format.</summary>
    [Fact]
    public void StableAcrossRuns()
    {
        var key = Fingerprint.Compute(_baseline);

        Assert.Equal(key, Fingerprint.Compute(_baseline with { }));
        Assert.Matches("^sha256:[0-9a-f]{64}$", key);
    }

    /// <summary>List order does not change the key.</summary>
    [Fact]
    public void ListOrderIgnored() =>
        Assert.Equal(Fingerprint.Compute(_baseline), Fingerprint.Compute(_baseline with { Hwaccels = ["qsv", "vaapi"] }));

    /// <summary>Changing any one component changes the key.</summary>
    [Fact]
    public void EachComponentChangesKey()
    {
        FingerprintInputs[] variants =
        [
            _baseline with { FfmpegPath = "/usr/bin/ffmpeg" },
            _baseline with { FfmpegVersionLine = "ffmpeg version 7.1.5-Jellyfin" },
            _baseline with { Hwaccels = ["vaapi"] },
            _baseline with { Devices = ["/dev/dri/renderD128", "/dev/dri/renderD129"] },
            _baseline with { DeviceIdentities = new Dictionary<string, string?> { ["/dev/dri/renderD128"] = "0x8086:0x46a6:i965" } },
            _baseline with { KernelRelease = "24.0.0" },
            _baseline with { OsPlatform = "windows" },
            _baseline with { OsVersion = "6.9.0" },
            _baseline with { ToolBuild = "a:b" },
            _baseline with { ToolBuild = "a:c" },
        ];

        var keys = variants.Select(Fingerprint.Compute).Append(Fingerprint.Compute(_baseline)).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Unreadable components hash as "unknown" and still give a stable key.</summary>
    [Fact]
    public void UnknownFieldsAreStable()
    {
        var sparse = new FingerprintInputs(null, null, null, null, new Dictionary<string, string?> { ["/dev/dri/renderD128"] = null }, null, null, null);

        Assert.Equal(Fingerprint.Compute(sparse), Fingerprint.Compute(sparse with { }));
        Assert.NotEqual(Fingerprint.Compute(sparse), Fingerprint.Compute(_baseline));
    }
}
