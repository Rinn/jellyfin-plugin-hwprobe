namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What one speed run measures: every chosen output from every chosen video.</summary>
/// <param name="Method">How streams are counted.</param>
/// <param name="Videos">Keys from <see cref="SpeedCatalog.Videos"/>, and <see cref="SpeedCatalog.LibraryKey"/> for <see cref="File"/>.</param>
/// <param name="Outputs">Keys from <see cref="SpeedCatalog.Outputs"/>.</param>
/// <param name="Comparisons">Optional runs beside each base result.</param>
/// <param name="Settings">The Jellyfin settings to start from.</param>
public sealed record SpeedOptions(SpeedMethod Method, IReadOnlyList<string> Videos, IReadOnlyList<string> Outputs, SpeedComparison Comparisons, SpeedSettings Settings)
{
    /// <summary>Gets how many times each measurement runs; more than once reports the median.</summary>
    public int Repeats { get; init; } = 1;

    /// <summary>Gets how long each measurement, repeats included, may take before it reports what it has, or null for no limit.</summary>
    public TimeSpan? TimeLimit { get; init; }

    /// <summary>Gets the library file the <c>library</c> video reads, or null.</summary>
    public SpeedFile? File { get; init; }

    /// <summary>Returns the tests to run, video by video, in the order asked.</summary>
    /// <returns>The tests; unknown keys are left out.</returns>
    public IReadOnlyList<SpeedTest> Resolve()
    {
        var videos = Videos.Select(k => k == SpeedCatalog.LibraryKey ? (File is null ? null : SpeedCatalog.LibraryVideo(File)) : SpeedCatalog.FindVideo(k)).OfType<SpeedVideo>().ToList();
        var outputs = Outputs.Select(SpeedCatalog.FindOutput).OfType<SpeedOutput>().ToList();
        return [.. videos.SelectMany(v => outputs.Select(o => SpeedCatalog.Test(v, o)))];
    }
}
