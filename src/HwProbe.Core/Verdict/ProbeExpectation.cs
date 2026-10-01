namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>What a probe must show to count as a hardware pass.</summary>
/// <param name="ExpectedFrames">Minimum final <c>frame=</c> count.</param>
/// <param name="ConfirmationStrings">Alternative stderr strings, any one of which confirms hardware use; empty means it cannot be confirmed.</param>
public sealed record ProbeExpectation(long ExpectedFrames, IReadOnlyList<string> ConfirmationStrings);
