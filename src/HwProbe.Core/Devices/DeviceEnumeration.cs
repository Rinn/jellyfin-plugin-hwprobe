namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>Every device candidate on the host, plus render-node facts for hints and the fingerprint.</summary>
/// <param name="Candidates">Candidates in probe order.</param>
/// <param name="RenderNodeAccess">Whether <c>/dev/dri</c> could be listed; null off Linux.</param>
/// <param name="RenderNodes">Identity of each render node found.</param>
public sealed record DeviceEnumeration(
    IReadOnlyList<DeviceCandidate> Candidates,
    DirectoryAccess? RenderNodeAccess,
    IReadOnlyList<RenderNodeIdentity> RenderNodes);
