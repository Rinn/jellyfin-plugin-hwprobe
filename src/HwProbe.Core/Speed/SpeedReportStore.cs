using System.Text.Json;
using Jellyfin.Plugin.HwProbe.Core.Storage;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Reads and writes speed reports.</summary>
public static class SpeedReportStore
{
    /// <summary>Serializes a report to JSON.</summary>
    /// <param name="report">The report.</param>
    /// <returns>Compact JSON.</returns>
    public static string Serialize(SpeedReport report) => JsonSerializer.Serialize(report, SpeedJsonContext.Files.SpeedReport);

    /// <summary>Parses a report, returning null for unreadable JSON.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The report, or null.</returns>
    public static SpeedReport? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, SpeedJsonContext.Default.SpeedReport);
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
    public static Task WriteAsync(SpeedReport report, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        return AtomicFile.WriteAllTextAsync(path, Serialize(report), cancellationToken);
    }
}
