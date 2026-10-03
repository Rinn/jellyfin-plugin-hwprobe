namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Reports progress on the caller's thread, in order.</summary>
/// <typeparam name="T">The progress value.</typeparam>
/// <param name="report">Applies one report.</param>
/// <remarks><see cref="Progress{T}"/> posts to the thread pool, so a late report could print after the run's output.</remarks>
internal sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
{
    /// <inheritdoc/>
    public void Report(T value) => report(value);
}
