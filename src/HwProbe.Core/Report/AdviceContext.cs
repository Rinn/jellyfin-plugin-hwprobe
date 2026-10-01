using Jellyfin.Plugin.HwProbe.Core.Devices;

namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>Facts about the host that decide which fixes apply.</summary>
/// <param name="Os">The host OS.</param>
/// <param name="InContainer">Whether the probe ran inside a container.</param>
/// <param name="OpenclUnavailable">Whether OpenCL failed to start on the device.</param>
public sealed record AdviceContext(HostOs Os, bool InContainer, bool OpenclUnavailable);
