namespace Jellyfin.Plugin.HwProbe.Core.Probes;

/// <summary>Runs probe units one at a time, restoring guarded environment variables after each.</summary>
/// <remarks>
/// <c>EncodingHelper.GetInputVideoHwaccelArgs</c> sets process-global environment variables, so arg
/// generation, launch and restore must be one critical section. Wrap the whole unit, not the launch alone.
/// </remarks>
public sealed class SerialProbeGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string[] _guardedVariables;

    /// <summary>Initializes a new instance of the <see cref="SerialProbeGate"/> class.</summary>
    /// <param name="guardedVariables">Environment variables snapshotted before and restored after each unit.</param>
    public SerialProbeGate(IEnumerable<string> guardedVariables)
    {
        ArgumentNullException.ThrowIfNull(guardedVariables);
        _guardedVariables = [.. guardedVariables];
    }

    /// <summary>Runs one unit exclusively.</summary>
    /// <typeparam name="T">The unit's result type.</typeparam>
    /// <param name="unit">The probe unit: generate args, launch, await exit.</param>
    /// <param name="cancellationToken">Cancels waiting for the gate and is passed to the unit.</param>
    /// <returns>The unit's result.</returns>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        await _gate.WaitAsync(cancellationToken);
        var snapshot = _guardedVariables.Select(name => (name, Environment.GetEnvironmentVariable(name))).ToList();
        try
        {
            return await unit(cancellationToken);
        }
        finally
        {
            foreach (var (name, value) in snapshot)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();
}
