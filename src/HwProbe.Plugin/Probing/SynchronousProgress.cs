namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>Reports on the caller's thread, in order, unlike <see cref="Progress{T}"/>.</summary>
/// <typeparam name="T">The progress value.</typeparam>
/// <param name="report">Applies one value.</param>
internal sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
{
    /// <inheritdoc/>
    public void Report(T value) => report(value);
}
