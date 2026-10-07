using System.Text;

namespace Jellyfin.Plugin.HwProbe.Core.Storage;

/// <summary>Replaces files whole, through a temporary file, so a reader never sees one half written.</summary>
public static class AtomicFile
{
    // Windows refuses to replace a file another request is reading; those reads take milliseconds.
    private const int Attempts = 5;
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Writes a file through a temporary file in the same folder, then moves it into place.</summary>
    /// <param name="path">The file.</param>
    /// <param name="write">Writes the content to the temporary file.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = File.Create(temp))
            {
                await write(file, cancellationToken);
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    return;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < Attempts)
                {
                    await Task.Delay(_retryDelay, cancellationToken);
                }
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>Writes text to a file as UTF-8, as <see cref="WriteAsync"/> does.</summary>
    /// <param name="path">The file.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public static Task WriteAllTextAsync(string path, string text, CancellationToken cancellationToken) =>
        WriteAsync(path, (file, ct) => file.WriteAsync(Encoding.UTF8.GetBytes(text), ct).AsTask(), cancellationToken);
}
