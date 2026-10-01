using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Probes;

/// <summary>The serial-execution invariant of <see cref="SerialProbeGate"/>.</summary>
public sealed class SerialProbeGateTests : IDisposable
{
    // Unique names: these tests mutate real process env while other tests run in parallel.
    private const string DriverVariable = "HWPROBE_TEST_GATE_DRIVER";
    private const string AmdVariable = "HWPROBE_TEST_GATE_AMD";

    private readonly FakeFfmpegHost _host = new();
    private readonly SerialProbeGate _gate = new([DriverVariable, AmdVariable]);

    /// <summary>Concurrent callers never overlap inside the gate.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConcurrentUnitsSerialise()
    {
        var inside = 0;
        var maxInside = 0;

        async Task<int> UnitAsync(CancellationToken ct)
        {
            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maxInside, now);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref inside);
            return now;
        }

        var ct = TestContext.Current.CancellationToken;
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => _gate.RunAsync(UnitAsync, ct), ct)));

        Assert.Equal(1, maxInside);
    }

    /// <summary>A unit's env side effect reaches its own child but not the next unit's, and the parent is restored.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    [Trait("Category", "FakeFfmpeg")]
    public async Task EnvSideEffectsDoNotLeakBetweenUnits()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new FfmpegRunner();
        var env = _host.Scenario(new { Default = new { EchoEnv = (string[])[DriverVariable, AmdVariable] } });
        var invocation = new FfmpegInvocation(FakeFfmpegHost.ExecutablePath, "-i x", env, TimeSpan.FromSeconds(30));
        var before = (Environment.GetEnvironmentVariable(DriverVariable), Environment.GetEnvironmentVariable(AmdVariable));

        // Mimics EncodingHelper: the i965 path sets one variable, the AMD path another.
        var first = await _gate.RunAsync(
            c =>
            {
                Environment.SetEnvironmentVariable(DriverVariable, "i965");
                return runner.RunAsync(invocation, c);
            },
            ct);
        var second = await _gate.RunAsync(
            c =>
            {
                Environment.SetEnvironmentVariable(AmdVariable, "noefc");
                return runner.RunAsync(invocation, c);
            },
            ct);

        Assert.Equal($"{DriverVariable}=i965\n{AmdVariable}!unset\n", first.Stdout);
        Assert.Equal($"{DriverVariable}!unset\n{AmdVariable}=noefc\n", second.Stdout);
        Assert.Equal(before, (Environment.GetEnvironmentVariable(DriverVariable), Environment.GetEnvironmentVariable(AmdVariable)));
    }

    /// <summary>A unit that throws still restores env and releases the gate.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ThrowingUnitRestoresAndReleases()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _gate.RunAsync<int>(
            _ =>
            {
                Environment.SetEnvironmentVariable(DriverVariable, "leaked");
                throw new InvalidOperationException("probe blew up");
            },
            ct));

        Assert.Null(Environment.GetEnvironmentVariable(DriverVariable));
        var next = await _gate.RunAsync(_ => Task.FromResult(42), ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(42, next);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _gate.Dispose();
        _host.Dispose();
    }

    /// <summary>Atomically raises a value to at least the candidate.</summary>
    /// <param name="target">The value to raise.</param>
    /// <param name="candidate">The candidate maximum.</param>
    private static void InterlockedMax(ref int target, int candidate)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < candidate
            && Interlocked.CompareExchange(ref target, candidate, current) != current)
        {
        }
    }
}
