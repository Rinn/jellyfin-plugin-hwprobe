namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What one speed run measures.</summary>
/// <param name="Method">How streams are counted.</param>
/// <param name="Tests">Keys from <see cref="SpeedCatalog"/>, in the order to run them.</param>
/// <param name="Comparisons">Optional runs beside each base result.</param>
/// <param name="Settings">The Jellyfin settings to start from.</param>
public sealed record SpeedOptions(SpeedMethod Method, IReadOnlyList<string> Tests, SpeedComparison Comparisons, SpeedSettings Settings)
{
    /// <summary>Gets a real file whose tests (<see cref="SpeedFileTests"/>) may be among <see cref="Tests"/>, or null.</summary>
    public SpeedFile? File { get; init; }

    /// <summary>Returns the tests to run, generated and on the file, in the order asked.</summary>
    /// <returns>The tests; unknown keys are left out.</returns>
    public IReadOnlyList<SpeedTest> Resolve()
    {
        var fileTests = File is null ? [] : SpeedFileTests.For(File);
        return [.. Tests.Select(k => SpeedCatalog.Find(k) ?? fileTests.FirstOrDefault(t => t.Key == k)).OfType<SpeedTest>()];
    }
}
