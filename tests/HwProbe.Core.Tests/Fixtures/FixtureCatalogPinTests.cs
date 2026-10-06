using System.Security.Cryptography;
using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>Pinned downloads and bundled clips in <see cref="FixtureCatalog"/>.</summary>
[Trait("Category", "Unit")]
public sealed class FixtureCatalogPinTests
{
    /// <summary>Every catalog sample is a FATE URL with a pinned hash; VC-1 is one, not a generated clip.</summary>
    [Fact]
    public void CatalogSamplesArePinnedFateUrls()
    {
        var downloadable = FixtureCatalog.All.Where(f => f.DownloadUrl is not null).ToList();

        Assert.Contains(FixtureCatalog.Vc1, downloadable);
        Assert.Null(FixtureCatalog.Vc1.UntestedReason);
        Assert.All(downloadable, f =>
        {
            Assert.StartsWith(Catalog.Default.Links["fateSuite"], f.DownloadUrl!.ToString(), StringComparison.Ordinal);
            Assert.Matches("^[0-9a-f]{64}$", f.Sha256);
        });
    }

    /// <summary>Every bundled clip ships in the assembly and matches its pinned hash.</summary>
    [Fact]
    public void BundledClipsMatchTheirPins()
    {
        var bundled = FixtureCatalog.All.Where(f => f.Bundled).ToList();

        Assert.NotEmpty(bundled);
        foreach (var spec in bundled)
        {
            using var resource = typeof(FixtureBuilder).Assembly.GetManifestResourceStream("Fixtures." + spec.FileName);
            Assert.NotNull(resource);
            Assert.Equal(spec.Sha256, Convert.ToHexStringLower(SHA256.HashData(resource)));
        }
    }
}
