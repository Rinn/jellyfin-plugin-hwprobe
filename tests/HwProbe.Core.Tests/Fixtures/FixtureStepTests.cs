using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>Status text from <see cref="FixtureStep.Describe"/>.</summary>
[Trait("Category", "Unit")]
public sealed class FixtureStepTests
{
    /// <summary>Making, downloading a large and a small clip, and a download of unknown size each read plainly.</summary>
    /// <param name="action">What's being done.</param>
    /// <param name="done">Bytes downloaded so far.</param>
    /// <param name="total">Bytes to download.</param>
    /// <param name="expected">The status text.</param>
    [Theory]
    [InlineData(FixtureAction.Generating, 0, 0, "Generating clip")]
    [InlineData(FixtureAction.Downloading, 5_200_000, 14_000_000, "Downloading clip: 5 of 14 MB")]
    [InlineData(FixtureAction.Downloading, 60_000, 125_000, "Downloading clip: 60 of 125 KB")]
    [InlineData(FixtureAction.Downloading, 0, 0, "Downloading clip")]
    public void Describes(FixtureAction action, long done, long total, string expected) =>
        Assert.Equal(expected, new FixtureStep(FixtureCatalog.Vc1, action, done, total).Describe("clip"));
}
