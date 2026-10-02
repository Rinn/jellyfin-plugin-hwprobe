using System.Net;
using System.Net.Http.Headers;

namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Downloads fixtures over HTTPS.</summary>
public sealed class HttpFixtureDownloader : IFixtureDownloader
{
    // Wikimedia asks for a descriptive User-Agent with a contact (meta.wikimedia.org/wiki/User-Agent_policy).
    private static readonly HttpClient _client = new() { DefaultRequestHeaders = { { "User-Agent", "HwProbe (https://github.com/Rinn/jellyfin-plugin-hwprobe)" } } };

    /// <inheritdoc/>
    public Task<byte[]> DownloadAsync(Uri url, CancellationToken cancellationToken) =>
        _client.GetByteArrayAsync(url, cancellationToken);

    /// <inheritdoc/>
    public async Task<byte[]> DownloadRangeAsync(Uri url, long start, long length, CancellationToken cancellationToken)
    {
        // HTTP ranges name the last byte, not a length.
        using var request = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new RangeHeaderValue(start, start + length - 1) } };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // A server that ignores the range sends the whole file, which would be the wrong bytes.
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new HttpRequestException($"{url} answered {(int)response.StatusCode} to a range request.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return bytes.LongLength == length ? bytes : throw new HttpRequestException($"{url} sent {bytes.LongLength} bytes, not {length}.");
    }
}
