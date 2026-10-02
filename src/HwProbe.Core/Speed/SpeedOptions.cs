namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What one speed run measures.</summary>
/// <param name="Method">How streams are counted.</param>
/// <param name="Tests">Keys from <see cref="SpeedCatalog"/>, in the order to run them.</param>
/// <param name="Comparisons">Optional runs beside each base result.</param>
/// <param name="Settings">The Jellyfin settings to start from.</param>
public sealed record SpeedOptions(SpeedMethod Method, IReadOnlyList<string> Tests, SpeedComparison Comparisons, SpeedSettings Settings);
