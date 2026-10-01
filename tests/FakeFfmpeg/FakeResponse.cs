namespace Jellyfin.Plugin.HwProbe.FakeFfmpeg;

/// <summary>What FakeFfmpeg does for one invocation. Every member is optional.</summary>
internal sealed record FakeResponse
{
    /// <summary>Gets literal text written to stdout.</summary>
    public string? Stdout { get; init; }

    /// <summary>Gets literal text written to stderr.</summary>
    public string? Stderr { get; init; }

    /// <summary>Gets a file (relative to the scenario file) whose contents go to stdout.</summary>
    public string? StdoutFile { get; init; }

    /// <summary>Gets a file (relative to the scenario file) whose contents go to stderr.</summary>
    public string? StderrFile { get; init; }

    /// <summary>Gets the number of filler bytes written to stdout, interleaved with stderr filler.</summary>
    public int StdoutFillBytes { get; init; }

    /// <summary>Gets the number of filler bytes written to stderr, interleaved with stdout filler.</summary>
    public int StderrFillBytes { get; init; }

    /// <summary>Gets the frame count reported as <c>-progress pipe:1</c> output, or null for none.</summary>
    public int? Frames { get; init; }

    /// <summary>Gets environment variables echoed to stdout as <c>NAME=value</c> or <c>NAME!unset</c>.</summary>
    /// <remarks>Nullable: source-generated JSON sets omitted init properties to null.</remarks>
    public IReadOnlyList<string>? EchoEnv { get; init; }

    /// <summary>Gets a value indicating whether to spawn a grandchild that hangs forever.</summary>
    public bool SpawnChild { get; init; }

    /// <summary>Gets a file that receives this process's PID and any grandchild's PID, one per line.</summary>
    public string? PidFile { get; init; }

    /// <summary>Gets a delay before exiting, in milliseconds.</summary>
    public int DelayMs { get; init; }

    /// <summary>Gets a value indicating whether to hang forever after writing output.</summary>
    public bool Hang { get; init; }

    /// <summary>Gets the process exit code.</summary>
    public int ExitCode { get; init; }
}
