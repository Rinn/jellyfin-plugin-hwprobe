using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>One video, as the page lists it.</summary>
/// <param name="Key">The video key.</param>
/// <param name="Name">What it's called.</param>
/// <param name="Input">What it is, e.g. <c>1080p H.264, 24 fps, 5.1 AAC</c>.</param>
/// <param name="Origin">Where it comes from, e.g. <c>Generated</c> or <c>10 MB download</c>.</param>
/// <param name="Interlaced">Whether it's interlaced, so the deinterlace comparison applies.</param>
/// <param name="Default">Whether it's chosen when the page first loads.</param>
/// <param name="Credit">The credit a sample's licence requires, or null.</param>
/// <param name="CreditHolder">Who holds the sample's rights, with the licence, or null.</param>
/// <param name="HolderUrl">The rights holder's site, named by its host in the credit, or null.</param>
/// <param name="LicenseUrl">The sample's licence, or null.</param>
/// <param name="SourceUrl">The page the sample comes from, or null.</param>
/// <param name="Title">The sample's title, or null.</param>
/// <param name="ArticleUrl">The Wikipedia article about the film, or null.</param>
public sealed record SpeedVideoInfo(string Key, string Name, string Input, string Origin, bool Interlaced, bool Default, string? Credit, string? CreditHolder, Uri? HolderUrl, Uri? LicenseUrl, Uri? SourceUrl, string? Title, Uri? ArticleUrl)
{
    /// <summary>Returns the page's view of a video.</summary>
    /// <param name="video">The video.</param>
    /// <returns>The view.</returns>
    public static SpeedVideoInfo From(SpeedVideo video)
    {
        ArgumentNullException.ThrowIfNull(video);
        var interlaced = video.File?.Video.Interlaced ?? video.Fixture?.Interlaced ?? false;
        return new(video.Key, video.Name, SpeedTestText.Input(video), video.Origin, interlaced, SpeedCatalog.DefaultVideos.Contains(video.Key), video.Credit, video.CreditHolder, video.HolderUrl, video.LicenseUrl, video.SourceUrl, video.Title, video.ArticleUrl);
    }
}
