namespace Jellyfin.Plugin.HwProbe.Core.Tests.Verdict;

/// <summary>Loads recorded ffmpeg output from <c>tests/Corpus</c>.</summary>
internal static class CorpusFile
{
    /// <summary>Reads a corpus file without its leading <c>#</c> header lines.</summary>
    /// <param name="relativePath">Path under the Corpus directory, e.g. <c>stderr/x.txt</c>.</param>
    /// <returns>The recorded output.</returns>
    public static string Load(string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Corpus", relativePath));
        return string.Join('\n', lines.SkipWhile(l => l.StartsWith('#'))) + "\n";
    }
}
