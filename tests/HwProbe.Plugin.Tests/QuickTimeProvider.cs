namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>The system clock with every timer cut to a millisecond, so code that polls on it runs at once.</summary>
internal sealed class QuickTimeProvider : TimeProvider
{
    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        System.CreateTimer(callback, state, TimeSpan.FromMilliseconds(1), period);
}
