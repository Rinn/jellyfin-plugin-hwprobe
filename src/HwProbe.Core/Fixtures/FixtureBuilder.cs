using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Generates fixture clips with the ffmpeg under test and caches them with a verified manifest.</summary>
public sealed class FixtureBuilder
{
    /// <summary>Default generation timeout; software 10-bit and AV1 encodes are slow on weak CPUs.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private const string ManifestSuffix = ".sha256";

    private readonly IFfmpegRunner _runner;
    private readonly string _ffmpegPath;
    private readonly string _cacheRoot;
    private readonly TimeSpan _timeout;
    private readonly IFixtureDownloader _downloader;
    private readonly IReadOnlyList<FixtureSpec> _catalog;
    private readonly string _downloadDirectory;

    /// <summary>Initializes a new instance of the <see cref="FixtureBuilder"/> class.</summary>
    /// <param name="runner">Launches ffmpeg.</param>
    /// <param name="ffmpegPath">The ffmpeg under test; fixtures must come from the same binary.</param>
    /// <param name="cacheRoot">Root fixture cache directory.</param>
    /// <param name="timeout">Per-fixture generation or download timeout.</param>
    /// <param name="downloader">Fetches fixtures that can't be generated.</param>
    public FixtureBuilder(IFfmpegRunner runner, string ffmpegPath, string cacheRoot, TimeSpan timeout, IFixtureDownloader downloader)
        : this(runner, ffmpegPath, cacheRoot, timeout, downloader, FixtureCatalog.All, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FixtureBuilder"/> class with a custom catalog.</summary>
    /// <param name="runner">Launches ffmpeg.</param>
    /// <param name="ffmpegPath">The ffmpeg under test.</param>
    /// <param name="cacheRoot">Root fixture cache directory.</param>
    /// <param name="timeout">Per-fixture timeout.</param>
    /// <param name="downloader">Fetches fixtures that can't be generated.</param>
    /// <param name="catalog">The fixtures to build.</param>
    /// <param name="downloadDirectory">Where downloads are kept, or null for <c>downloads</c> under the cache root.</param>
    internal FixtureBuilder(IFfmpegRunner runner, string ffmpegPath, string cacheRoot, TimeSpan timeout, IFixtureDownloader downloader, IReadOnlyList<FixtureSpec> catalog, string? downloadDirectory)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        _downloader = downloader;
        _catalog = catalog;

        // Downloads don't depend on the ffmpeg under test, so they're shared across cache keys.
        _downloadDirectory = downloadDirectory ?? Path.Combine(cacheRoot, "downloads");
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrEmpty(ffmpegPath);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);

        _runner = runner;
        _ffmpegPath = ffmpegPath;
        _cacheRoot = cacheRoot;
        _timeout = timeout;
    }

    /// <summary>Ensures every buildable fixture exists in the cache for this key, generating as needed.</summary>
    /// <param name="cacheKey">Cache partition, normally the host fingerprint.</param>
    /// <param name="availableEncoders">Encoder names in this ffmpeg build, from build enumeration.</param>
    /// <param name="cancellationToken">Cancels generation.</param>
    /// <returns>One result per fixture, in catalog order.</returns>
    public async Task<IReadOnlyList<FixtureResult>> BuildAsync(
        string cacheKey,
        IReadOnlySet<string> availableEncoders,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        ArgumentNullException.ThrowIfNull(availableEncoders);

        var directory = Path.Combine(_cacheRoot, SanitizeKey(cacheKey));
        Directory.CreateDirectory(directory);

        var results = new List<FixtureResult>(_catalog.Count);
        foreach (var spec in _catalog)
        {
            results.Add(await ResolveAsync(spec, directory, availableEncoders, cancellationToken));
        }

        return results;
    }

    /// <summary>Replaces characters that are invalid in a directory name, such as the colon in <c>sha256:</c>.</summary>
    /// <param name="cacheKey">The raw key.</param>
    /// <returns>A key safe to use as a directory name on every OS.</returns>
    private static string SanitizeKey(string cacheKey)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(cacheKey.Select(c => c == ':' || invalid.Contains(c) ? '_' : c));
    }

    /// <summary>Reports whether a cached fixture matches its manifest and was made from this spec.</summary>
    /// <param name="path">The fixture path.</param>
    /// <param name="spec">The fixture spec it must have been encoded from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True when the file and manifest both exist and size, hash and recipe match.</returns>
    private static async Task<bool> IsValidAsync(string path, FixtureSpec spec, CancellationToken cancellationToken)
    {
        var manifestPath = path + ManifestSuffix;
        if (!File.Exists(path) || !File.Exists(manifestPath))
        {
            return false;
        }

        var expected = (await File.ReadAllTextAsync(manifestPath, cancellationToken)).Trim();
        return expected == await DescribeAsync(path, spec, cancellationToken);
    }

    /// <summary>Returns the manifest line for a file: its size, SHA-256, and the hash of the recipe that made it.</summary>
    /// <param name="path">The file.</param>
    /// <param name="spec">The spec it was encoded from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>{size} {sha256-hex} {recipe-sha256-hex}</c>.</returns>
    /// <remarks>The recipe hash makes a changed encode argument regenerate the fixture instead of reusing it.</remarks>
    private static async Task<string> DescribeAsync(string path, FixtureSpec spec, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var recipe = SHA256.HashData(Encoding.UTF8.GetBytes($"{spec.RequiredEncoder}\n{spec.EncodeArguments}"));
        return string.Create(CultureInfo.InvariantCulture, $"{stream.Length} {Convert.ToHexStringLower(hash)} {Convert.ToHexStringLower(recipe)}");
    }

    /// <summary>Summarises a failed encode for the report.</summary>
    /// <param name="result">The ffmpeg result.</param>
    /// <returns>A one-line reason.</returns>
    private static string Describe(FfmpegRunResult result)
    {
        var lastLine = result.Stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return result.Status switch
        {
            FfmpegRunStatus.LaunchFailed => $"ffmpeg failed to launch: {result.LaunchError}",
            FfmpegRunStatus.TimedOut => "fixture generation timed out",
            _ when result.ExitCode != 0 => $"ffmpeg exited {result.ExitCode}: {lastLine}",
            _ => "ffmpeg exited 0 but wrote no output",
        };
    }

    /// <summary>Deletes a file if present.</summary>
    /// <param name="path">The file.</param>
    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Returns a cached fixture, or generates it.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="directory">The cache directory for this key.</param>
    /// <param name="availableEncoders">Encoders in this build.</param>
    /// <param name="cancellationToken">Cancels generation.</param>
    /// <returns>The fixture's state.</returns>
    private async Task<FixtureResult> ResolveAsync(
        FixtureSpec spec,
        string directory,
        IReadOnlySet<string> availableEncoders,
        CancellationToken cancellationToken)
    {
        if (spec.UntestedReason is not null)
        {
            return new FixtureResult(spec, FixtureStatus.Untested, null, spec.UntestedReason);
        }

        if (spec.DownloadUrl is { } url)
        {
            return await DownloadAsync(spec, url, Path.Combine(_downloadDirectory, spec.Sha256 + Path.GetExtension(spec.FileName)), cancellationToken);
        }

        if (spec.RequiredEncoder is { } encoder && !availableEncoders.Contains(encoder))
        {
            return new FixtureResult(spec, FixtureStatus.Skipped, null, $"software encoder {encoder} is not in this ffmpeg build");
        }

        var path = Path.Combine(directory, spec.FileName);
        if (await IsValidAsync(path, spec, cancellationToken))
        {
            return new FixtureResult(spec, FixtureStatus.Available, path, null);
        }

        return await GenerateAsync(spec, path, cancellationToken);
    }

    /// <summary>Returns a cached downloaded fixture, or downloads it and checks its pinned hash.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="url">Where to download it from.</param>
    /// <param name="path">Final cached path.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>Available, or Untested with the reason the sample couldn't be fetched.</returns>
    private async Task<FixtureResult> DownloadAsync(FixtureSpec spec, Uri url, string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken))) == spec.Sha256)
        {
            return new FixtureResult(spec, FixtureStatus.Available, path, null);
        }

        byte[] bytes;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            bytes = await _downloader.DownloadAsync(url, timeout.Token);
        }
        catch (HttpRequestException ex)
        {
            return new FixtureResult(spec, FixtureStatus.Untested, null, $"could not download the {spec.Codec} sample from {url}: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FixtureResult(spec, FixtureStatus.Untested, null, $"downloading the {spec.Codec} sample from {url} timed out");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (hash != spec.Sha256)
        {
            return new FixtureResult(spec, FixtureStatus.Untested, null, $"the {spec.Codec} sample from {url} has SHA-256 {hash}, not the pinned {spec.Sha256}");
        }

        Directory.CreateDirectory(_downloadDirectory);
        var partial = path + ".partial";
        await File.WriteAllBytesAsync(partial, bytes, cancellationToken);
        File.Move(partial, path, overwrite: true);
        return new FixtureResult(spec, FixtureStatus.Available, path, null);
    }

    /// <summary>Encodes a fixture to a temp name, then moves it into place and writes its manifest.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="path">Final cached path.</param>
    /// <param name="cancellationToken">Cancels generation.</param>
    /// <returns>The fixture's state.</returns>
    private async Task<FixtureResult> GenerateAsync(FixtureSpec spec, string path, CancellationToken cancellationToken)
    {
        // Keep the extension so ffmpeg still infers the container.
        var partial = Path.ChangeExtension(path, ".partial" + Path.GetExtension(path));
        var manifestPath = path + ManifestSuffix;
        DeleteIfExists(manifestPath);
        DeleteIfExists(partial);

        var arguments = $"{spec.EncodeArguments} \"{partial}\"";
        var invocation = new FfmpegInvocation(_ffmpegPath, arguments, new Dictionary<string, string?>(), _timeout);
        var result = await _runner.RunAsync(invocation, cancellationToken);

        var succeeded = result.Status == FfmpegRunStatus.Exited
            && result.ExitCode == 0
            && File.Exists(partial)
            && new FileInfo(partial).Length > 0;
        if (!succeeded)
        {
            DeleteIfExists(partial);
            return new FixtureResult(spec, FixtureStatus.Failed, null, Describe(result));
        }

        // Manifest last: a run killed before this leaves a file that won't validate.
        File.Move(partial, path, overwrite: true);
        var manifest = await DescribeAsync(path, spec, cancellationToken);
        var manifestPartial = manifestPath + ".partial";
        await File.WriteAllTextAsync(manifestPartial, manifest, cancellationToken);
        File.Move(manifestPartial, manifestPath, overwrite: true);

        return new FixtureResult(spec, FixtureStatus.Available, path, null);
    }
}
