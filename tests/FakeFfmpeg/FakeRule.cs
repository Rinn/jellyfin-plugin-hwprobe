namespace Jellyfin.Plugin.HwProbe.FakeFfmpeg;

/// <summary>A response selected when the joined arguments contain <see cref="Match"/>.</summary>
internal sealed record FakeRule
{
    /// <summary>Gets the substring searched for in the space-joined arguments.</summary>
    public required string Match { get; init; }

    /// <summary>Gets the response used when the rule matches.</summary>
    public required FakeResponse Response { get; init; }
}
