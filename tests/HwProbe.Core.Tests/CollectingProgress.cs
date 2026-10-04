namespace Jellyfin.Plugin.HwProbe.Core.Tests;

/// <summary>Keeps every report, on the reporting thread, so a test sees them in order.</summary>
/// <typeparam name="T">The report type.</typeparam>
internal sealed class CollectingProgress<T> : IProgress<T>
{
    private readonly List<T> _reports = [];

    /// <summary>Gets the reports, in order.</summary>
    public IReadOnlyList<T> Reports => _reports;

    /// <inheritdoc/>
    public void Report(T value) => _reports.Add(value);
}
