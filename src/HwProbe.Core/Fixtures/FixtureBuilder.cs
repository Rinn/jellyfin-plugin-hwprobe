using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>Generates fixture clips with the ffmpeg under test and caches them with a verified manifest.</summary>
public sealed partial class FixtureBuilder
{
    /// <summary>Default generation timeout; software 10-bit and AV1 encodes are slow on weak CPUs.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private const string ManifestSuffix = ".sha256";

    // How long a piece download may go without receiving anything.
    private static readonly TimeSpan _downloadStall = TimeSpan.FromSeconds(60);

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

    /// <summary>Gets what receives each download and encode of a clip that isn't cached, or null.</summary>
    internal IProgress<FixtureStep>? Progress { get; init; }

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
    internal static string SanitizeKey(string cacheKey)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(cacheKey.Select(c => c == ':' || invalid.Contains(c) ? '_' : c));
    }

    /// <summary>Counts the fixtures <see cref="BuildAsync"/> would make or download, leaving out those already cached.</summary>
    /// <param name="cacheKey">Cache partition, as <see cref="BuildAsync"/> takes it.</param>
    /// <param name="availableEncoders">Encoder names in this ffmpeg build.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>How many fixtures require work, so progress counts only those.</returns>
    internal async Task<int> PendingAsync(string cacheKey, IReadOnlySet<string> availableEncoders, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        ArgumentNullException.ThrowIfNull(availableEncoders);
        var directory = Path.Combine(_cacheRoot, SanitizeKey(cacheKey));
        var pending = 0;
        foreach (var spec in _catalog)
        {
            pending += await IsPendingAsync(spec, directory, availableEncoders, cancellationToken) ? 1 : 0;
        }

        return pending;
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

        // A re-pinned piece changes the recipe, so a clip made from the old one is fetched again.
        var text = $"{spec.RequiredEncoder}\n{spec.EncodeArguments}" + (spec.FallbackArguments is null ? string.Empty : $"\n{spec.FallbackArguments}")
            + (spec.Piece is { } piece ? string.Create(CultureInfo.InvariantCulture, $"\n{piece.Url} {piece.HeaderLength} {piece.Start} {piece.Length} {piece.Sha256}") : string.Empty);
        var recipe = SHA256.HashData(Encoding.UTF8.GetBytes(text));
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
            _ when result.ExitCode != 0 => string.Create(CultureInfo.InvariantCulture, $"ffmpeg exited {result.ExitCode}: {lastLine}"),
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

    /// <summary>Matches <c>{clip:name}</c>, another clip in the same cache directory, made before this one.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\{clip:([^}]+)\}")]
    private static partial Regex ClipPlaceholder();

    /// <summary>Reports whether resolving a fixture would make or download it, following <see cref="ResolveAsync"/>'s order.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="directory">The cache partition.</param>
    /// <param name="availableEncoders">Encoder names in this ffmpeg build.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>False for a cached fixture, one that can't be tested, and a bundled copy, which is written without a step.</returns>
    private async Task<bool> IsPendingAsync(FixtureSpec spec, string directory, IReadOnlySet<string> availableEncoders, CancellationToken cancellationToken)
    {
        if (spec.UntestedReason is not null)
        {
            return false;
        }

        if (spec.RequiredEncoder is null && spec.DownloadUrl is { } url)
        {
            return !await IsDownloadedAsync(spec, url, cancellationToken);
        }

        if (spec.RequiredEncoder is { } encoder && !availableEncoders.Contains(encoder))
        {
            return !spec.Bundled && spec.DownloadUrl is { } fallback && !await IsDownloadedAsync(spec, fallback, cancellationToken);
        }

        return !await IsValidAsync(Path.Combine(directory, spec.FileName), spec, cancellationToken);
    }

    /// <summary>Returns where a download is cached.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="url">Where it's downloaded from.</param>
    /// <returns>The path, named by the pinned SHA-256.</returns>
    private string DownloadPath(FixtureSpec spec, Uri url) => Path.Combine(_downloadDirectory, spec.Sha256 + Path.GetExtension(url.AbsolutePath));

    /// <summary>Reports whether a download is cached with its pinned hash.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="url">Where it's downloaded from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True when the cached file matches.</returns>
    private async Task<bool> IsDownloadedAsync(FixtureSpec spec, Uri url, CancellationToken cancellationToken)
    {
        var path = DownloadPath(spec, url);
        return File.Exists(path) && Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken))) == spec.Sha256;
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

        if (spec.RequiredEncoder is null && spec.DownloadUrl is { } url)
        {
            return await DownloadAsync(spec, url, cancellationToken);
        }

        if (spec.RequiredEncoder is { } encoder && !availableEncoders.Contains(encoder))
        {
            return await OrDownloadAsync(spec, new FixtureResult(spec, FixtureStatus.Skipped, null, $"software encoder {encoder} is not in this ffmpeg build"), cancellationToken);
        }

        var path = Path.Combine(directory, spec.FileName);
        if (await IsValidAsync(path, spec, cancellationToken))
        {
            return new FixtureResult(spec, FixtureStatus.Available, path, null);
        }

        // A piece with no encode is the clip itself.
        if (spec.Piece is { } exact && spec.EncodeArguments.Length == 0)
        {
            if (await FetchPieceAsync(spec, exact, path, cancellationToken) is { } missing)
            {
                return missing;
            }

            await File.WriteAllTextAsync(path + ManifestSuffix, await DescribeAsync(path, spec, cancellationToken), cancellationToken);
            return new FixtureResult(spec, FixtureStatus.Available, path, null);
        }

        string? piece = null;
        if (spec.Piece is { } wanted)
        {
            piece = path + ".piece.webm";
            if (await FetchPieceAsync(spec, wanted, piece, cancellationToken) is { } failure)
            {
                return failure;
            }
        }

        FixtureResult generated;
        try
        {
            Progress?.Report(new FixtureStep(spec, FixtureAction.Generating, 0, 0));
            generated = await GenerateAsync(spec, path, piece, cancellationToken);
        }
        finally
        {
            // The encode is cached; the piece is only its source, and can be 100 MB.
            if (piece is not null)
            {
                DeleteIfExists(piece);
            }
        }

        return generated.Status == FixtureStatus.Available ? generated : await OrDownloadAsync(spec, generated, cancellationToken);
    }

    /// <summary>Downloads a pinned piece of a large file in two requests and checks its hash.</summary>
    /// <param name="spec">The fixture made from it.</param>
    /// <param name="piece">The piece.</param>
    /// <param name="path">Where to save it.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>Null when it's saved; otherwise why the fixture can't be made.</returns>
    private async Task<FixtureResult?> FetchPieceAsync(FixtureSpec spec, FixturePiece piece, string path, CancellationToken cancellationToken)
    {
        byte[] header;
        byte[] body;
        try
        {
            // A slow download that keeps arriving is let finish; one that stops is given up.
            using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stalled.CancelAfter(_downloadStall);
            Progress?.Report(new FixtureStep(spec, FixtureAction.Downloading, 0, piece.Size));
            header = await _downloader.DownloadRangeAsync(piece.Url, 0, piece.HeaderLength, null, stalled.Token);
            var bodyProgress = new StepProgress(n =>
            {
                stalled.CancelAfter(_downloadStall);
                Progress?.Report(new FixtureStep(spec, FixtureAction.Downloading, piece.HeaderLength + n, piece.Size));
            });
            body = await _downloader.DownloadRangeAsync(piece.Url, piece.Start, piece.Length, bodyProgress, stalled.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // A dropped connection mid-body is an IOException (HttpIOException), not an HttpRequestException.
            return new FixtureResult(spec, FixtureStatus.Untested, null, $"could not download {piece.Url}: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FixtureResult(spec, FixtureStatus.Untested, null, string.Create(CultureInfo.InvariantCulture, $"downloading {piece.Url} stopped for {_downloadStall.TotalSeconds:0} s"));
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData([.. header, .. body]));
        if (hash != piece.Sha256)
        {
            // The host may have re-encoded the file; a changed piece isn't used.
            return new FixtureResult(spec, FixtureStatus.Untested, null, $"the piece of {piece.Url} has SHA-256 {hash}, not the pinned {piece.Sha256}");
        }

        await using (var file = File.Create(path + ".partial"))
        {
            await file.WriteAsync(header, cancellationToken);
            await file.WriteAsync(body, cancellationToken);
        }

        File.Move(path + ".partial", path, overwrite: true);
        return null;
    }

    /// <summary>Falls back to the bundled copy, then the pinned sample, when the clip couldn't be generated.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="failure">Why it couldn't be generated.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The bundled or downloaded clip, or the original failure with the download's reason added.</returns>
    private async Task<FixtureResult> OrDownloadAsync(FixtureSpec spec, FixtureResult failure, CancellationToken cancellationToken)
    {
        if (spec.Bundled)
        {
            return await ExtractBundledAsync(spec, failure, cancellationToken);
        }

        if (spec.DownloadUrl is not { } url)
        {
            return failure;
        }

        var downloaded = await DownloadAsync(spec, url, cancellationToken);
        return downloaded.Status == FixtureStatus.Available ? downloaded : failure with { Reason = $"{failure.Reason}; download: {downloaded.Reason}" };
    }

    /// <summary>Writes the bundled copy of a clip to the download cache, checking its pinned hash.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="failure">Why it couldn't be generated.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Available, or the original failure when the bundled copy is missing or doesn't match.</returns>
    private async Task<FixtureResult> ExtractBundledAsync(FixtureSpec spec, FixtureResult failure, CancellationToken cancellationToken)
    {
        await using var resource = typeof(FixtureBuilder).Assembly.GetManifestResourceStream("Fixtures." + spec.FileName);
        if (resource is null)
        {
            return failure with { Reason = $"{failure.Reason}; no bundled copy of {spec.FileName}" };
        }

        using var buffer = new MemoryStream();
        await resource.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (hash != spec.Sha256)
        {
            return failure with { Reason = $"{failure.Reason}; bundled {spec.FileName} has SHA-256 {hash}, not the pinned {spec.Sha256}" };
        }

        var path = Path.Combine(_downloadDirectory, spec.Sha256 + Path.GetExtension(spec.FileName));
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(_downloadDirectory);
            var partial = path + ".partial";
            await File.WriteAllBytesAsync(partial, bytes, cancellationToken);
            File.Move(partial, path, overwrite: true);
        }

        return new FixtureResult(spec, FixtureStatus.Available, path, null);
    }

    /// <summary>Returns a cached downloaded fixture, or downloads it and checks its pinned hash.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="url">Where to download it from.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>Available, or Untested with the reason the sample couldn't be fetched.</returns>
    /// <remarks>Cached by hash with the URL's extension, since a sample's format can differ from the generated clip's.</remarks>
    private async Task<FixtureResult> DownloadAsync(FixtureSpec spec, Uri url, CancellationToken cancellationToken)
    {
        var path = DownloadPath(spec, url);
        if (await IsDownloadedAsync(spec, url, cancellationToken))
        {
            return new FixtureResult(spec, FixtureStatus.Available, path, null);
        }

        byte[] bytes;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            Progress?.Report(new FixtureStep(spec, FixtureAction.Downloading, 0, 0));
            bytes = await _downloader.DownloadAsync(url, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
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

    /// <summary>Runs one encode to the temp path.</summary>
    /// <param name="arguments">ffmpeg arguments before the output path.</param>
    /// <param name="partial">The temp output path.</param>
    /// <param name="timeout">The time limit.</param>
    /// <param name="cancellationToken">Cancels the encode.</param>
    /// <returns>Null when it wrote output; otherwise why it failed, with the temp file removed.</returns>
    private async Task<string?> EncodeAsync(string arguments, string partial, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var invocation = new FfmpegInvocation(_ffmpegPath, $"{arguments} \"{partial}\"", new Dictionary<string, string?>(), timeout);
        var result = await _runner.RunAsync(invocation, cancellationToken);
        if (result.Status == FfmpegRunStatus.Exited && result.ExitCode == 0 && File.Exists(partial) && new FileInfo(partial).Length > 0)
        {
            return null;
        }

        DeleteIfExists(partial);
        return Describe(result);
    }

    /// <summary>Encodes a fixture to a temp name, then moves it into place and writes its manifest.</summary>
    /// <param name="spec">The fixture.</param>
    /// <param name="path">Final cached path.</param>
    /// <param name="piece">The downloaded piece the encode reads, or null.</param>
    /// <param name="cancellationToken">Cancels generation.</param>
    /// <returns>The fixture's state.</returns>
    private async Task<FixtureResult> GenerateAsync(FixtureSpec spec, string path, string? piece, CancellationToken cancellationToken)
    {
        // Keep the extension so ffmpeg still infers the container.
        var partial = Path.ChangeExtension(path, ".partial" + Path.GetExtension(path));
        var manifestPath = path + ManifestSuffix;
        DeleteIfExists(manifestPath);
        DeleteIfExists(partial);

        var timeout = spec.GenerateTimeout ?? _timeout;
        var arguments = piece is null ? spec.EncodeArguments : spec.EncodeArguments.Replace("{piece}", $"\"{piece}\"", StringComparison.Ordinal);
        arguments = ClipPlaceholder().Replace(arguments, m => $"\"{Path.Combine(Path.GetDirectoryName(path)!, m.Groups[1].Value)}\"");
        var result = await EncodeAsync(arguments, partial, timeout, cancellationToken);
        if (result is not null && spec.FallbackArguments is not null)
        {
            var fallback = await EncodeAsync(spec.FallbackArguments, partial, timeout, cancellationToken);
            result = fallback is null ? null : $"{result}; retry: {fallback}";
        }

        if (result is not null)
        {
            return new FixtureResult(spec, FixtureStatus.Failed, null, result);
        }

        // Manifest last: a run killed before this leaves a file that won't validate.
        File.Move(partial, path, overwrite: true);
        var manifest = await DescribeAsync(path, spec, cancellationToken);
        var manifestPartial = manifestPath + ".partial";
        await File.WriteAllTextAsync(manifestPartial, manifest, cancellationToken);
        File.Move(manifestPartial, manifestPath, overwrite: true);

        return new FixtureResult(spec, FixtureStatus.Available, path, null);
    }

    /// <summary>Reports byte counts on the caller's thread, in order.</summary>
    /// <param name="report">Applies one count.</param>
    /// <remarks><see cref="Progress{T}"/> posts to the thread pool, so counts could arrive out of order.</remarks>
    private sealed class StepProgress(Action<long> report) : IProgress<long>
    {
        /// <inheritdoc/>
        public void Report(long value) => report(value);
    }
}
