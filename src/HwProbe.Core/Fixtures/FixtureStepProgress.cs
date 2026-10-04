namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Reports clip steps on the caller's thread, in order; <see cref="Progress{T}"/> would post them to the thread pool, out of order.</summary>
/// <param name="report">Applies one step.</param>
internal sealed class FixtureStepProgress(Action<FixtureStep> report) : IProgress<FixtureStep>
{
    /// <inheritdoc/>
    public void Report(FixtureStep value) => report(value);
}
