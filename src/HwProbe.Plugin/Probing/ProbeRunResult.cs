namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>What happened to a request to probe.</summary>
public enum ProbeRunResult
{
    /// <summary>The probe ran and its report was saved.</summary>
    Completed,

    /// <summary>The probe was accepted and is running in the background.</summary>
    Started,

    /// <summary>Another probe is already running.</summary>
    AlreadyRunning,

    /// <summary>Refused: a session is transcoding, so results would be wrong.</summary>
    ServerBusy,

    /// <summary>The probe failed; see <see cref="ProbeStatus.LastError"/>.</summary>
    Failed,

    /// <summary>Refused: a speed run needs a probe's report to know which backends work.</summary>
    NoReport,

    /// <summary>Refused: the request named an unknown method, test or comparison.</summary>
    Invalid,
}
