namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>Every video and output a speed run can use.</summary>
/// <param name="Videos">The videos.</param>
/// <param name="Outputs">The outputs.</param>
public sealed record SpeedCatalogInfo(IReadOnlyList<SpeedVideoInfo> Videos, IReadOnlyList<SpeedOutputInfo> Outputs);
