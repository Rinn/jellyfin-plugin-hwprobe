using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Ffmpeg;

/// <summary>Discovery-order and failure behaviour of <see cref="FfmpegLocator"/>.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class FfmpegLocatorTests
{
    /// <summary>The command-line path wins over every other source.</summary>
    [Fact]
    public void ExplicitPathWinsOverEverything()
    {
        var locator = Create(
            env: new() { ["JELLYFIN_FFMPEG"] = "/env/ffmpeg", ["PATH"] = "/bin" },
            files: [Full("/cli/ffmpeg"), Full("/env/ffmpeg"), "/usr/lib/jellyfin-ffmpeg/ffmpeg", Path.Combine("/bin", "ffmpeg")]);

        Assert.Equal(new FfmpegLocation(Full("/cli/ffmpeg"), FfmpegSource.CommandLine), locator.Locate("/cli/ffmpeg"));
    }

    /// <summary>The environment variable wins when no command-line path is given.</summary>
    [Fact]
    public void EnvironmentVariableWinsOverKnownPaths()
    {
        var locator = Create(
            env: new() { ["JELLYFIN_FFMPEG"] = "/env/ffmpeg" },
            files: [Full("/env/ffmpeg"), "/usr/lib/jellyfin-ffmpeg/ffmpeg"]);

        Assert.Equal(new FfmpegLocation(Full("/env/ffmpeg"), FfmpegSource.EnvironmentVariable), locator.Locate(null));
    }

    /// <summary>A missing command-line binary throws instead of falling through.</summary>
    [Fact]
    public void MissingExplicitPathThrows()
    {
        var locator = Create(env: new() { ["PATH"] = "/bin" }, files: ["/bin/ffmpeg"]);

        var ex = Assert.Throws<FileNotFoundException>(() => locator.Locate("/nope/ffmpeg"));
        Assert.Contains("CommandLine", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A missing environment-variable binary throws instead of falling through.</summary>
    [Fact]
    public void MissingEnvironmentPathThrows()
    {
        var locator = Create(
            env: new() { ["JELLYFIN_FFMPEG"] = "/nope/ffmpeg" },
            files: ["/usr/lib/jellyfin-ffmpeg/ffmpeg"]);

        var ex = Assert.Throws<FileNotFoundException>(() => locator.Locate(null));
        Assert.Contains("EnvironmentVariable", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Known install locations are tried in the documented order.</summary>
    /// <param name="present">Known paths that exist on the fake host.</param>
    /// <param name="expected">The path that should be chosen.</param>
    [Theory]
    [InlineData(new[] { "/usr/lib/jellyfin-ffmpeg/ffmpeg", "/usr/lib/jellyfin/bin/ffmpeg" }, "/usr/lib/jellyfin-ffmpeg/ffmpeg")]
    [InlineData(new[] { "/usr/lib/jellyfin/bin/ffmpeg", "/Applications/Jellyfin.app/Contents/MacOS/ffmpeg" }, "/usr/lib/jellyfin/bin/ffmpeg")]
    [InlineData(new[] { "/Applications/Jellyfin.app/Contents/MacOS/ffmpeg" }, "/Applications/Jellyfin.app/Contents/MacOS/ffmpeg")]
    public void KnownPathsFollowDiscoveryOrder(string[] present, string expected)
    {
        var locator = Create(env: new() { ["PATH"] = "/bin" }, files: [.. present, "/bin/ffmpeg"]);

        Assert.Equal(new FfmpegLocation(expected, FfmpegSource.KnownPath), locator.Locate(null));
    }

    /// <summary>PATH is searched last, in entry order, skipping empty entries.</summary>
    [Fact]
    public void SystemPathSearchedInOrder()
    {
        var locator = Create(
            env: new() { ["PATH"] = "/a::/b:/c" },
            files: [Path.Combine("/b", "ffmpeg"), Path.Combine("/c", "ffmpeg")]);

        Assert.Equal(new FfmpegLocation(Path.Combine("/b", "ffmpeg"), FfmpegSource.SystemPath), locator.Locate(null));
    }

    /// <summary>Windows uses the semicolon separator and the .exe suffix.</summary>
    [Fact]
    public void WindowsUsesSemicolonAndExe()
    {
        var expected = Path.Combine(@"C:\b", "ffmpeg.exe");
        var locator = Create(
            env: new() { ["PATH"] = @"C:\a;C:\b" },
            files: [expected],
            isWindows: true);

        Assert.Equal(new FfmpegLocation(expected, FfmpegSource.SystemPath), locator.Locate(null));
    }

    /// <summary>No source yielding a binary returns null rather than throwing.</summary>
    [Fact]
    public void NothingFoundReturnsNull()
    {
        var locator = Create(env: new() { ["PATH"] = "/bin" }, files: []);

        Assert.Null(locator.Locate(null));
    }

    /// <summary>A relative command-line path is reported as absolute.</summary>
    [Fact]
    public void RelativeExplicitPathIsMadeAbsolute()
    {
        var expected = Path.GetFullPath("tools/ffmpeg");
        var locator = Create(env: [], files: [expected]);

        Assert.Equal(new FfmpegLocation(expected, FfmpegSource.CommandLine), locator.Locate("tools/ffmpeg"));
    }

    /// <summary>Resolves a path the way the locator does on the machine running the tests.</summary>
    /// <param name="path">A Unix-style test path.</param>
    /// <returns>The absolute path, e.g. <c>D:\cli\ffmpeg</c> on Windows.</returns>
    private static string Full(string path) => Path.GetFullPath(path);

    /// <summary>Builds a locator over a fake environment and filesystem.</summary>
    /// <param name="env">Environment variables visible to the locator.</param>
    /// <param name="files">Paths that exist.</param>
    /// <param name="isWindows">Whether to behave as Windows.</param>
    /// <returns>The locator under test.</returns>
    private static FfmpegLocator Create(Dictionary<string, string> env, string[] files, bool isWindows = false)
    {
        var existing = new HashSet<string>(files, StringComparer.Ordinal);
        return new FfmpegLocator(name => env.GetValueOrDefault(name), existing.Contains, isWindows);
    }
}
