namespace Jellyfin.Plugin.HwProbe.Core.Tests.Speed;

/// <summary>A clock that moves only when told to.</summary>
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock on.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;
}
