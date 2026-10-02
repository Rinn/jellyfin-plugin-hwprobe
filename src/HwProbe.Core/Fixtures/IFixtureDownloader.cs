namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Fetches a fixture that can't be generated locally.</summary>
public interface IFixtureDownloader
{
    /// <summary>Downloads a file.</summary>
    /// <param name="url">Where to fetch it from.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The file's bytes.</returns>
    Task<byte[]> DownloadAsync(Uri url, CancellationToken cancellationToken);

    /// <summary>Downloads part of a file.</summary>
    /// <param name="url">Where to fetch it from.</param>
    /// <param name="start">The offset of the first byte.</param>
    /// <param name="length">How many bytes.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The bytes.</returns>
    Task<byte[]> DownloadRangeAsync(Uri url, long start, long length, CancellationToken cancellationToken);
}
