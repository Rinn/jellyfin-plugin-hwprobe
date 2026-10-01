namespace Jellyfin.Plugin.HwProbe.FakeFfmpeg;

/// <summary>A scenario file: ordered rules, a fallback, and an optional invocation log.</summary>
internal sealed record FakeScenario
{
    // Nullable: source-generated JSON sets omitted init properties to null.

    /// <summary>Gets the rules, tried in order; the first match wins.</summary>
    public IReadOnlyList<FakeRule>? Rules { get; init; }

    /// <summary>Gets the response used when no rule matches.</summary>
    public FakeResponse? Default { get; init; }

    /// <summary>Gets a file that receives one JSON line per invocation, for asserting call order.</summary>
    public string? LogFile { get; init; }
}
