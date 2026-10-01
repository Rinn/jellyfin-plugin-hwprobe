namespace Jellyfin.Plugin.HwProbe.FakeFfmpeg;

/// <summary>One logged invocation, written as a JSON line to the scenario's log file.</summary>
/// <param name="Args">The space-joined arguments.</param>
/// <param name="Pid">The FakeFfmpeg process ID.</param>
/// <param name="StartUtc">When the invocation began.</param>
/// <param name="EndUtc">When the invocation finished writing and was about to exit.</param>
internal sealed record FakeInvocation(string Args, int Pid, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
