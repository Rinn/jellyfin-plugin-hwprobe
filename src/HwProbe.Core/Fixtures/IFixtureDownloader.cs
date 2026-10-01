namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Fetches a fixture that can't be generated locally.</summary>
public interface IFixtureDownloader
{
    /// <summary>Downloads a file.</summary>
    /// <param name="url">Where to fetch it from.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The file's bytes.</returns>
    Task<byte[]> DownloadAsync(Uri url, CancellationToken cancellationToken);
}
