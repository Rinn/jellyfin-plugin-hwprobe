using Jellyfin.Plugin.HwProbe.Core.Fixtures;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Fixtures;

/// <summary>A downloader that returns scripted bytes, or fails like an offline host.</summary>
/// <param name="bytes">The bytes to return, or null to fail.</param>
internal sealed class ScriptedDownloader(byte[]? bytes) : IFixtureDownloader
{
    /// <summary>Gets a downloader that fails as if the host were offline.</summary>
    public static ScriptedDownloader Offline { get; } = new(null);

    /// <summary>Gets how many downloads were attempted.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc/>
    public Task<byte[]> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        Calls++;
        return bytes is null ? throw new HttpRequestException("network is unreachable") : Task.FromResult(bytes);
    }

    /// <inheritdoc/>
    public Task<byte[]> DownloadRangeAsync(Uri url, long start, long length, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        Calls++;
        return bytes is null ? throw new HttpRequestException("network is unreachable") : Task.FromResult(bytes[(int)start..(int)(start + length)]);
    }
}
