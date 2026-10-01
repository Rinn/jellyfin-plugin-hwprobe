namespace Jellyfin.Plugin.HwProbe.Jellyfin;

/// <summary>Records every member EncodingHelper reaches on its dependencies.</summary>
public sealed class CallRecorder
{
    private readonly List<string> _calls = [];
    private readonly List<string> _unexpected = [];
    private readonly Lock _lock = new();

    /// <summary>Gets every member hit, as <c>Interface.Member</c>, in call order.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Gets members hit that had no handler; non-empty means upstream reached code we don't model.</summary>
    public IReadOnlyList<string> Unexpected
    {
        get
        {
            lock (_lock)
            {
                return [.. _unexpected];
            }
        }
    }

    /// <summary>Records a handled call.</summary>
    /// <param name="member">The <c>Interface.Member</c> name.</param>
    internal void Record(string member)
    {
        lock (_lock)
        {
            _calls.Add(member);
        }
    }

    /// <summary>Records a call with no handler.</summary>
    /// <param name="member">The <c>Interface.Member</c> name.</param>
    internal void RecordUnexpected(string member)
    {
        lock (_lock)
        {
            _calls.Add(member);
            _unexpected.Add(member);
        }
    }
}
