namespace Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

/// <summary>Finds the ffmpeg binary to probe, in the documented discovery order.</summary>
public sealed class FfmpegLocator
{
    /// <summary>Environment variable naming the ffmpeg binary, as honoured by the Jellyfin server.</summary>
    public const string EnvironmentVariableName = "JELLYFIN_FFMPEG";

    private static readonly string[] _knownPaths =
    [
        "/usr/lib/jellyfin-ffmpeg/ffmpeg",
        "/usr/lib/jellyfin/bin/ffmpeg",
        "/Applications/Jellyfin.app/Contents/MacOS/ffmpeg",
    ];

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string, bool> _fileExists;
    private readonly bool _isWindows;

    /// <summary>Initializes a new instance of the <see cref="FfmpegLocator"/> class against the real host.</summary>
    public FfmpegLocator()
        : this(Environment.GetEnvironmentVariable, File.Exists, OperatingSystem.IsWindows())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FfmpegLocator"/> class with injected host access.</summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable; returns null when unset.</param>
    /// <param name="fileExists">Reports whether a file exists at the given path.</param>
    /// <param name="isWindows">Selects the Windows <c>PATH</c> separator and <c>.exe</c> suffix.</param>
    public FfmpegLocator(Func<string, string?> getEnvironmentVariable, Func<string, bool> fileExists, bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(fileExists);

        _getEnvironmentVariable = getEnvironmentVariable;
        _fileExists = fileExists;
        _isWindows = isWindows;
    }

    /// <summary>Resolves the ffmpeg binary.</summary>
    /// <param name="explicitPath">The <c>--ffmpeg</c> value, or null when not given.</param>
    /// <returns>The resolved binary, or null when no source yields one.</returns>
    /// <exception cref="FileNotFoundException">An explicitly named binary does not exist.</exception>
    public FfmpegLocation? Locate(string? explicitPath)
    {
        // A missing explicit path is an error; falling through would probe a different binary.
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Require(explicitPath, FfmpegSource.CommandLine);
        }

        var fromEnvironment = _getEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return Require(fromEnvironment, FfmpegSource.EnvironmentVariable);
        }

        foreach (var path in _knownPaths)
        {
            if (_fileExists(path))
            {
                return new FfmpegLocation(path, FfmpegSource.KnownPath);
            }
        }

        return SearchSystemPath();
    }

    /// <summary>Resolves an explicitly named binary, failing loudly if it is missing.</summary>
    /// <param name="path">The named path, possibly relative.</param>
    /// <param name="source">Which explicit source named it.</param>
    /// <returns>The resolved binary.</returns>
    private FfmpegLocation Require(string path, FfmpegSource source)
    {
        var fullPath = Path.GetFullPath(path);
        if (!_fileExists(fullPath))
        {
            throw new FileNotFoundException($"ffmpeg not found at '{fullPath}' (from {source}).", fullPath);
        }

        return new FfmpegLocation(fullPath, source);
    }

    /// <summary>Returns the first <c>ffmpeg</c> found on <c>PATH</c>.</summary>
    /// <returns>The binary, or null when <c>PATH</c> has none.</returns>
    private FfmpegLocation? SearchSystemPath()
    {
        var searchPath = _getEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(searchPath))
        {
            return null;
        }

        var separator = _isWindows ? ';' : ':';
        var fileName = _isWindows ? "ffmpeg.exe" : "ffmpeg";
        foreach (var directory in searchPath.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, fileName);
            if (_fileExists(candidate))
            {
                return new FfmpegLocation(candidate, FfmpegSource.SystemPath);
            }
        }

        return null;
    }
}
