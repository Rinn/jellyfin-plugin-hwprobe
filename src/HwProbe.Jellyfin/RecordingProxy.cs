using System.Reflection;

namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>Interface proxy that records every call and throws for members without a handler.</summary>
/// <remarks>Public and unsealed because <see cref="DispatchProxy"/> generates a subclass at runtime.</remarks>
public class RecordingProxy : DispatchProxy
{
    private CallRecorder _recorder = new();
    private string _interfaceName = string.Empty;
    private IReadOnlyDictionary<string, Func<object?[], object?>> _handlers = new Dictionary<string, Func<object?[], object?>>();

    /// <summary>Creates a recording proxy for <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The interface to proxy.</typeparam>
    /// <param name="recorder">Receives every call.</param>
    /// <param name="handlers">Handlers keyed by method name (<c>get_X</c> for properties).</param>
    /// <returns>The proxy.</returns>
    public static T Create<T>(CallRecorder recorder, IReadOnlyDictionary<string, Func<object?[], object?>> handlers)
        where T : class
    {
        var proxy = Create<T, RecordingProxy>();
        var recording = (RecordingProxy)(object)proxy;
        recording._recorder = recorder;
        recording._interfaceName = typeof(T).Name;
        recording._handlers = handlers;
        return proxy;
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var member = $"{_interfaceName}.{targetMethod.Name}";
        if (_handlers.TryGetValue(targetMethod.Name, out var handler))
        {
            _recorder.Record(member);
            return handler(args ?? []);
        }

        _recorder.RecordUnexpected(member);
        throw new NotSupportedException($"{member} is not modelled by the probe stubs.");
    }
}
