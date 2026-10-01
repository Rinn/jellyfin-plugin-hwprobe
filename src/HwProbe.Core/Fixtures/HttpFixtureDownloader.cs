namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Downloads fixtures over HTTPS.</summary>
public sealed class HttpFixtureDownloader : IFixtureDownloader
{
    private static readonly HttpClient _client = new();

    /// <inheritdoc/>
    public Task<byte[]> DownloadAsync(Uri url, CancellationToken cancellationToken) =>
        _client.GetByteArrayAsync(url, cancellationToken);
}
