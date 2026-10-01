using System.Text.Json;

namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>Reads and writes reports, and caches them by fingerprint.</summary>
/// <param name="cacheDirectory">Directory holding cached reports.</param>
public sealed class ReportStore(string cacheDirectory)
{
    /// <summary>Serializes a report to JSON.</summary>
    /// <param name="report">The report.</param>
    /// <returns>Indented JSON.</returns>
    public static string Serialize(CapabilityReport report) =>
        JsonSerializer.Serialize(report, ReportJsonContext.Default.CapabilityReport);

    /// <summary>Parses a report, returning null for unreadable JSON or another schema version.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The report, or null.</returns>
    public static CapabilityReport? Deserialize(string json)
    {
        try
        {
            var report = JsonSerializer.Deserialize(json, ReportJsonContext.Default.CapabilityReport);
            return report?.SchemaVersion == CapabilityReport.CurrentSchemaVersion ? report : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes a report to a file, atomically.</summary>
    /// <param name="report">The report.</param>
    /// <param name="path">Destination file.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public static async Task WriteAsync(CapabilityReport report, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(temp, Serialize(report), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Returns the cached report for a fingerprint, or null on a miss.</summary>
    /// <param name="fingerprint">The cache key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The cached report, or null.</returns>
    public async Task<CapabilityReport?> LoadCachedAsync(string fingerprint, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fingerprint);
        var path = PathFor(fingerprint);
        if (!File.Exists(path))
        {
            return null;
        }

        var report = Deserialize(await File.ReadAllTextAsync(path, cancellationToken));
        return report?.Fingerprint == fingerprint ? report : null;
    }

    /// <summary>Caches a report under its fingerprint.</summary>
    /// <param name="report">The report.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the report is cached.</returns>
    public Task SaveCachedAsync(CapabilityReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        return WriteAsync(report, PathFor(report.Fingerprint), cancellationToken);
    }

    /// <summary>Maps a fingerprint to a cache file name that is valid on every OS.</summary>
    /// <param name="fingerprint">The cache key.</param>
    /// <returns>The cache file path.</returns>
    private string PathFor(string fingerprint) =>
        Path.Combine(cacheDirectory, fingerprint.Replace(':', '_') + ".json");
}
