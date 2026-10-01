namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>Advice for one option on Jellyfin's Transcoding page.</summary>
/// <param name="Section">The heading the option sits under on that page.</param>
/// <param name="Setting">
/// The <c>EncodingOptions</c> property, or <c>HardwareDecodingCodecs:&lt;codec&gt;</c> for one codec in the decoding list.
/// </param>
/// <param name="Label">The option's label on the Transcoding page.</param>
/// <param name="State">What the tests say.</param>
/// <param name="Note">Short reason, e.g. <c>Not supported by this GPU</c>; empty when the test passed.</param>
public sealed record SettingAdvice(string Section, string Setting, string Label, SettingState State, string Note)
{
    /// <summary>Gets a short fix for an option that could work after a change on the host, when there is one.</summary>
    public Fix? Fix { get; init; }
}
