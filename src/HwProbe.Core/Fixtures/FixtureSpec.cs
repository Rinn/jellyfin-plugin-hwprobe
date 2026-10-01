namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>One fixture clip: how to generate it and the stream properties a synthetic job needs.</summary>
/// <param name="FileName">Cached file name; its extension picks the container.</param>
/// <param name="Codec">Codec name as Jellyfin's <c>MediaStream.Codec</c> reports it.</param>
/// <param name="BitDepth">Luma bit depth.</param>
/// <param name="IsHdr10">Whether the clip carries BT.2020 PQ (HDR10) metadata.</param>
/// <param name="RequiredEncoder">Software encoder that must be in the build, or null when none can make it.</param>
/// <param name="EncodeArguments">ffmpeg arguments before the output path.</param>
/// <param name="UntestedReason">Why the fixture can never be generated, or null when it can.</param>
public sealed record FixtureSpec(
    string FileName,
    string Codec,
    int BitDepth,
    bool IsHdr10,
    string? RequiredEncoder,
    string EncodeArguments,
    string? UntestedReason);
