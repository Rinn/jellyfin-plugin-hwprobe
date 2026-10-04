using System.Globalization;

namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>One step of making or downloading a clip that isn't cached.</summary>
/// <param name="Spec">The clip.</param>
/// <param name="Action">What's being done.</param>
/// <param name="Done">Bytes downloaded so far; 0 while making.</param>
/// <param name="Total">Bytes to download, or 0 when unknown or making.</param>
public sealed record FixtureStep(FixtureSpec Spec, FixtureAction Action, long Done, long Total)
{
    /// <summary>Describes the step for a status line.</summary>
    /// <param name="name">What the clip is called.</param>
    /// <returns>e.g. <c>Downloading Animation: 5 of 14 MB</c>.</returns>
    public string Describe(string name) =>
        Action == FixtureAction.Generating ? "Generating " + name
        : Total >= 1_000_000 ? string.Create(CultureInfo.InvariantCulture, $"Downloading {name}: {Done / 1_000_000} of {Math.Round(Total / 1_000_000.0):0} MB")
        : Total > 0 ? string.Create(CultureInfo.InvariantCulture, $"Downloading {name}: {Done / 1000} of {Math.Round(Total / 1000.0):0} KB")
        : "Downloading " + name;
}
