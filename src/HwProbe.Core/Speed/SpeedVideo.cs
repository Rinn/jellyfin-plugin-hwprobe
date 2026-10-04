using Jellyfin.Plugin.HwProbe.Core.Fixtures;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>A video a speed run reads: a clip made on the server, a downloaded sample, or a library file.</summary>
/// <param name="Key">Stable key, e.g. <c>pattern</c> or <c>live-action</c>.</param>
/// <param name="Name">What the page calls it.</param>
/// <param name="Fixture">The generated or downloaded clip, or null for a library file.</param>
/// <param name="FrameRate">Its frame rate.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
public sealed record SpeedVideo(string Key, string Name, FixtureSpec? Fixture, float FrameRate, int Width, int Height)
{
    /// <summary>Gets the library file, or null for a clip.</summary>
    public SpeedFile? File { get; init; }

    /// <summary>Gets its audio as the page describes it, e.g. <c>5.1 AAC</c>, or null for none.</summary>
    public string? Audio { get; init; }

    /// <summary>Gets where it comes from, e.g. <c>Generated</c> or <c>10 MB download</c>.</summary>
    public string Origin { get; init; } = string.Empty;

    /// <summary>Gets the credit a sample's licence requires, or null.</summary>
    public string? Credit { get; init; }

    /// <summary>Gets who holds the sample's rights, with the licence, or null.</summary>
    public string? CreditHolder { get; init; }

    /// <summary>Gets the sample's title, e.g. <c>Tears of Steel</c>, or null.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the page the sample comes from, with its credit and licence, or null.</summary>
    public Uri? SourceUrl { get; init; }

    /// <summary>Gets the Wikipedia article about the film, or null.</summary>
    public Uri? ArticleUrl { get; init; }

    /// <summary>Gets the sample's licence, or null.</summary>
    public Uri? LicenseUrl { get; init; }
}
