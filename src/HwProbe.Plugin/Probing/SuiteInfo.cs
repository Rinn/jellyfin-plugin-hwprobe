using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>A test suite as this server would run it.</summary>
/// <param name="Key">The catalog key.</param>
/// <param name="Name">What the page calls it.</param>
/// <param name="Description">What it finds out.</param>
/// <param name="Steps">The steps' labels, in order.</param>
/// <param name="Backends">The backends it runs on; software is <see cref="HwType.none"/>.</param>
/// <param name="Offered">Whether it can run here: a probe has run and the server has what it needs.</param>
public sealed record SuiteInfo(string Key, string Name, string Description, IReadOnlyList<string> Steps, IReadOnlyList<HwType> Backends, bool Offered);
