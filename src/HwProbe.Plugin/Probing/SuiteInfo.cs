using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Speed;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>A test suite as this server would run it.</summary>
/// <param name="Key">The catalog key.</param>
/// <param name="Name">What the page calls it.</param>
/// <param name="Description">What it finds out.</param>
/// <param name="Steps">The steps' labels, in order.</param>
/// <param name="Backends">The backends it runs on; software is <see cref="HwType.none"/>.</param>
/// <param name="Offered">Whether it can run here: a probe has run and the server has what it needs.</param>
/// <param name="Method">The accuracy its steps measure at.</param>
/// <param name="Note">Advice shown with the suite and its results, or null.</param>
/// <param name="Measurements">How many measurements its steps make together: inputs times outputs times backends, per step.</param>
public sealed record SuiteInfo(string Key, string Name, string Description, IReadOnlyList<string> Steps, IReadOnlyList<HwType> Backends, bool Offered, int Measurements, SpeedMethod Method, string? Note);
