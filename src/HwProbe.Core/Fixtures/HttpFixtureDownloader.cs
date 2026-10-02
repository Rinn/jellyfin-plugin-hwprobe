using System.Net;
using System.Net.Http.Headers;

namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Downloads fixtures over HTTPS.</summary>
public sealed class HttpFixtureDownloader : IFixtureDownloader
{
    // Wikimedia asks for a descriptive User-Agent with a contact (meta.wikimedia.org/wiki/User-Agent_policy).
    private static readonly HttpClient _client = new() { DefaultRequestHeaders = { { "User-Agent", "HwProbe (https://github.com/Rinn/jellyfin-plugin-hwprobe)" } } };

    /// <summary>Gets a value indicating whether downloads are turned off with <c>HWPROBE_NO_DOWNLOADS=1</c>, as tests and CI run.</summary>
    private static bool Disabled => Environment.GetEnvironmentVariable("HWPROBE_NO_DOWNLOADS") == "1";

    /// <inheritdoc/>
    public Task<byte[]> DownloadAsync(Uri url, CancellationToken cancellationToken) =>
        Disabled ? throw Off(url) : _client.GetByteArrayAsync(url, cancellationToken);

    /// <inheritdoc/>
    public async Task<byte[]> DownloadRangeAsync(Uri url, long start, long length, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (Disabled)
        {
            throw Off(url);
        }

        // HTTP ranges name the last byte, not a length.
        using var request = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new RangeHeaderValue(start, start + length - 1) } };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // A server that ignores the range sends the whole file, which would be the wrong bytes.
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new HttpRequestException($"{url} answered {(int)response.StatusCode} to a range request.");
        }

        var bytes = new byte[length];
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var received = 0;
        while (received < length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(received), cancellationToken);
            if (read == 0)
            {
                break;
            }

            received += read;
            progress?.Report(received);
        }

        return received == length && await stream.ReadAsync(new byte[1], cancellationToken) == 0 ? bytes : throw new HttpRequestException($"{url} sent a different length from the {length} bytes asked for.");
    }

    /// <summary>The error for a download refused because downloads are off.</summary>
    /// <param name="url">The refused file.</param>
    /// <returns>The exception, reported like any failed download.</returns>
    private static HttpRequestException Off(Uri url) => new($"downloads are turned off (HWPROBE_NO_DOWNLOADS), so {url} wasn't fetched");
}
