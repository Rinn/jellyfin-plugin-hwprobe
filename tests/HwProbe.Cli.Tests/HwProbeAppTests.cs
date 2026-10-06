using System.IO.Compression;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Cli.Tests;

/// <summary>Exit codes and output of a full CLI run against FakeFfmpeg.</summary>
[Trait("Category", "FakeFfmpeg")]
[Trait("Category", "Platform")]
public sealed class HwProbeAppTests : IDisposable
{
    private readonly FakeFfmpegHost _host = new();

    /// <summary>A software-only host prints its table and exits 0 bare, or 1 under --expect-hw.</summary>
    /// <param name="expectHw">Whether --expect-hw is given.</param>
    /// <param name="expected">The exit code.</param>
    /// <returns>A task representing the test.</returns>
    [Theory(Skip = "Requires Linux or macOS: the ffmpeg wrapper is a shell script.", SkipUnless = nameof(TestEnvironment.IsPosix), SkipType = typeof(TestEnvironment))]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task NoHardwareExitCode(bool expectHw, int expected)
    {
        var (code, stdout, _) = await RunAsync(SoftwareOnly(), expectHw ? ["--expect-hw"] : []);

        Assert.Equal(expected, code);
        Assert.Contains("TYPE", stdout, StringComparison.Ordinal);
    }

    /// <summary>--format json round-trips and carries schemaVersion; --json writes the same report.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact(Skip = "Requires Linux or macOS: the ffmpeg wrapper is a shell script.", SkipUnless = nameof(TestEnvironment.IsPosix), SkipType = typeof(TestEnvironment))]
    public async Task JsonOutputRoundTrips()
    {
        var file = _host.PathFor("report.json");
        var (code, stdout, _) = await RunAsync(SoftwareOnly(), ["--format", "json", "--json", file]);

        Assert.Equal(0, code);
        Assert.Contains($"\"schemaVersion\": {CapabilityReport.CurrentSchemaVersion}", stdout, StringComparison.Ordinal);
        var report = ReportStore.Deserialize(stdout);
        Assert.NotNull(report);
        Assert.Equal(report.Fingerprint, ReportStore.Deserialize(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!.Fingerprint);
    }

    /// <summary>--diagnostics writes a zip with the report and the capability listings.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact(Skip = "Requires Linux or macOS: the ffmpeg wrapper is a shell script.", SkipUnless = nameof(TestEnvironment.IsPosix), SkipType = typeof(TestEnvironment))]
    public async Task DiagnosticsZipIsWritten()
    {
        var file = _host.PathFor("diagnostics.zip");
        var (code, _, stderr) = await RunAsync(SoftwareOnly(), ["--diagnostics", file]);

        Assert.Equal(0, code);
        Assert.Contains("hardware-report.yml", stderr, StringComparison.Ordinal);
        using var zip = await ZipFile.OpenReadAsync(file, TestContext.Current.CancellationToken);
        Assert.Contains(zip.Entries, e => e.FullName == "report.json");
        Assert.Contains(zip.Entries, e => e.FullName == "ffmpeg/hwaccels.txt");
    }

    /// <summary>An ffmpeg below 4.4 exits 2.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact(Skip = "Requires Linux or macOS: the ffmpeg wrapper is a shell script.", SkipUnless = nameof(TestEnvironment.IsPosix), SkipType = typeof(TestEnvironment))]
    public async Task OldFfmpegExitsTwo()
    {
        var env = _host.Scenario(new { Rules = new[] { new { Match = "-version", Response = new { Stdout = "ffmpeg version 4.3 Copyright (c) 2000-2020\n" } } } });
        var (code, _, stderr) = await RunAsync(env, []);

        Assert.Equal(2, code);
        Assert.Contains("TooOld", stderr, StringComparison.Ordinal);
    }

    /// <summary>A named ffmpeg that doesn't exist exits 2.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MissingFfmpegExitsTwo()
    {
        var command = new HwProbeCommand();
        var options = command.Bind(command.Root.Parse(["--ffmpeg", _host.PathFor("nope")]));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var code = await HwProbeApp.RunAsync(options, new FfmpegLocator(), new HostPlatform(), _host.Directory, stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(2, code);
    }

    /// <inheritdoc/>
    public void Dispose() => _host.Dispose();

    /// <summary>A scenario for a valid ffmpeg with no hardware backends built.</summary>
    /// <returns>Environment selecting the scenario.</returns>
    private Dictionary<string, string?> SoftwareOnly() => _host.Scenario(new
    {
        Rules = new[]
        {
            new { Match = "-version", Response = new { Stdout = "ffmpeg version 7.1.4 Copyright (c) 2000-2025 the FFmpeg developers\n" } },
            new { Match = "-hwaccels", Response = new { Stdout = "Hardware acceleration methods:\n\n" } },
        },
    });

    /// <summary>Runs the CLI against FakeFfmpeg with the scenario environment applied to this process.</summary>
    /// <param name="scenario">Environment naming the scenario.</param>
    /// <param name="args">Extra arguments.</param>
    /// <returns>Exit code, stdout and stderr.</returns>
    private async Task<(int Code, string Stdout, string Stderr)> RunAsync(Dictionary<string, string?> scenario, string[] args)
    {
        // Build enumeration launches carry no env overrides, so a wrapper script fixes the scenario per test.
        var wrapper = WriteWrapper(scenario["HWPROBE_FAKE_SCENARIO"]!);
        var command = new HwProbeCommand();
        var options = command.Bind(command.Root.Parse([.. args, "--ffmpeg", wrapper, "--fixtures", _host.PathFor("fixtures")]));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var code = await HwProbeApp.RunAsync(options, new FfmpegLocator(), new HostPlatform(), _host.Directory, stdout, stderr, TestContext.Current.CancellationToken);
        return (code, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Writes a shell wrapper that runs FakeFfmpeg with a fixed scenario.</summary>
    /// <param name="scenarioPath">The scenario file.</param>
    /// <returns>The wrapper path.</returns>
    private string WriteWrapper(string scenarioPath)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The ffmpeg wrapper is a shell script.");
        }

        var path = _host.PathFor($"ffmpeg-{Guid.NewGuid():N}");
        File.WriteAllText(path, $"#!/bin/sh\nHWPROBE_FAKE_SCENARIO='{scenarioPath}' exec '{FakeFfmpegHost.ExecutablePath}' \"$@\"\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
