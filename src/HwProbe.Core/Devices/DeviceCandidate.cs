using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Devices;

/// <summary>A device to try to open for one backend.</summary>
/// <param name="Type">The backend.</param>
/// <param name="Device">Render node path, adapter or CUDA index, or empty when the backend takes no selector.</param>
public sealed record DeviceCandidate(HwType Type, string Device);
