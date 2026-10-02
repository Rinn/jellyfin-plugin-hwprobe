using Jellyfin.Plugin.HwProbe.Core.Speed;
using Jellyfin.Plugin.HwProbe.Probing;
using Jellyfin.Plugin.HwProbe.Settings;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>Admin API for running probes, reading the report, and applying its advice.</summary>
/// <param name="service">The probe runner.</param>
/// <param name="settings">The settings writer.</param>
[ApiController]
[Route("HwProbe")]
[Authorize(Policy = Policies.RequiresElevation)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HwProbeController(ProbeService service, SettingsService settings) : ControllerBase
{
    /// <summary>Returns the latest report.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The report JSON, or 404 when no probe has completed.</returns>
    [HttpGet("Report")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetReportAsync(CancellationToken cancellationToken)
    {
        var json = await service.LatestJsonAsync(cancellationToken);
        return json is null ? NotFound() : Content(json, "application/json");
    }

    /// <summary>Returns a zip of the latest probe's report and ffmpeg logs, to attach to an issue.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The zip, or 404 when no probe has completed.</returns>
    [HttpGet("Diagnostics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var zip = await service.LatestDiagnosticsAsync(cancellationToken);
        return zip is null ? NotFound() : File(zip, "application/zip", "hwprobe-diagnostics.zip");
    }

    /// <summary>Returns whether a probe is running and how the last one ended.</summary>
    /// <returns>The status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ProbeStatus> CurrentStatus() => service.Status;

    /// <summary>Starts a probe in the background.</summary>
    /// <param name="cancellationToken">Cancels the busy check.</param>
    /// <returns>202 when started; 409 when one is running or a session is transcoding.</returns>
    [HttpPost("Run")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RunAsync(CancellationToken cancellationToken) => await service.StartAsync(cancellationToken) switch
    {
        ProbeRunResult.Started => Accepted(),
        ProbeRunResult.AlreadyRunning => Conflict("A probe or speed run is already running."),
        ProbeRunResult.ServerBusy => Conflict("A session is transcoding; probe when the server is idle."),
        var other => Problem($"Unexpected result {other}."),
    };

    /// <summary>Starts measuring the speed of the backends the latest report found working, and software.</summary>
    /// <param name="request">The method, tests and comparisons.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>202 when started; 400 for an unknown name; 409 when busy or no probe has run.</returns>
    [HttpPost("Speed")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> StartSpeedAsync([FromBody] SpeedRequest request, CancellationToken cancellationToken) => await service.StartSpeedAsync(request, cancellationToken) switch
    {
        ProbeRunResult.Started => Accepted(),
        ProbeRunResult.Invalid => BadRequest("Unknown method, test or comparison."),
        ProbeRunResult.NoReport => Conflict("Run a probe first, so the speed run knows which backends work."),
        ProbeRunResult.AlreadyRunning => Conflict("A probe or speed run is already running."),
        ProbeRunResult.ServerBusy => Conflict("A session is transcoding; measure when the server is idle."),
        var other => Problem($"Unexpected result {other}."),
    };

    /// <summary>Returns every video and output a speed run can use, for the page's choices.</summary>
    /// <returns>The videos and outputs.</returns>
    [HttpGet("SpeedCatalog")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SpeedCatalogInfo> SpeedCatalog() =>
        Ok(new SpeedCatalogInfo([.. Core.Speed.SpeedCatalog.Videos.Select(SpeedVideoInfo.From)], [.. Core.Speed.SpeedCatalog.Outputs.Select(SpeedOutputInfo.From)]));

    /// <summary>Describes a library item's file as a speed run video.</summary>
    /// <param name="itemId">The movie or episode.</param>
    /// <returns>The video, or 404 when it isn't a local video file.</returns>
    [HttpGet("SpeedLibraryVideo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SpeedVideoInfo> SpeedLibraryVideo([FromQuery] Guid itemId) =>
        service.FindFile(itemId) is { } file ? Ok(SpeedVideoInfo.From(Core.Speed.SpeedCatalog.LibraryVideo(file))) : NotFound();

    /// <summary>Returns the running speed run's results so far.</summary>
    /// <returns>The partial speed report, or 404 when none is running.</returns>
    [HttpGet("SpeedProgress")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult SpeedProgress() => service.RunningSpeedJson() is { } json ? Content(json, "application/json") : NotFound();

    /// <summary>Lists the saved speed runs, newest first.</summary>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The runs.</returns>
    [HttpGet("SpeedHistory")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SpeedHistoryEntry>>> SpeedHistoryAsync(CancellationToken cancellationToken) =>
        Ok(await service.SpeedHistoryAsync(cancellationToken));

    /// <summary>Returns one saved speed run.</summary>
    /// <param name="id">The run.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The speed report JSON, or 404.</returns>
    [HttpGet("SpeedHistory/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SpeedHistoryRunAsync([FromRoute] string id, CancellationToken cancellationToken) =>
        await service.SpeedHistoryJsonAsync(id, cancellationToken) is { } json ? Content(json, "application/json") : NotFound();

    /// <summary>Returns the size of the cached test clips and samples.</summary>
    /// <returns>Bytes and files.</returns>
    [HttpGet("Cache")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<CacheSize> Cache() => service.FixtureCacheSize();

    /// <summary>Deletes the cached test clips and samples.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>204 when deleted; 409 while a probe or speed run uses them.</returns>
    [HttpDelete("Cache")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> PurgeCacheAsync(CancellationToken cancellationToken) =>
        await service.PurgeFixtureCacheAsync(cancellationToken) ? NoContent() : Conflict("A probe or speed run is using the cache.");

    /// <summary>Returns the latest speed report.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The speed report JSON, or 404 when none was measured with this HwProbe and ffmpeg.</returns>
    [HttpGet("Speed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetSpeedAsync(CancellationToken cancellationToken)
    {
        var json = await service.LatestSpeedJsonAsync(cancellationToken);
        return json is null ? NotFound() : Content(json, "application/json");
    }

    /// <summary>Applies options from the latest report's advice for the configured backend.</summary>
    /// <param name="changes">The options and values.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The result; 400 when it doesn't match the report, 409 when busy or no probe has run.</returns>
    [HttpPost("Apply")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplyResult>> ApplyAsync([FromBody] IReadOnlyList<SettingChange> changes, CancellationToken cancellationToken) =>
        ToResponse(await settings.ApplyAsync(changes, UserName(), cancellationToken));

    /// <summary>Switches the hardware acceleration backend and device to one the latest report found working, or to software (<c>none</c>).</summary>
    /// <param name="choice">The backend and device.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The result, with <c>RestartRequired</c> set.</returns>
    [HttpPost("UseBackend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplyResult>> UseBackendAsync([FromBody] BackendChoice choice, CancellationToken cancellationToken) =>
        ToResponse(await settings.UseBackendAsync(choice, UserName(), cancellationToken));

    /// <summary>Undoes the most recent apply or backend switch.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The result; 409 when there's nothing to revert or a probe is running.</returns>
    [HttpPost("Revert")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplyResult>> RevertAsync(CancellationToken cancellationToken) =>
        ToResponse(await settings.RevertAsync(UserName(), cancellationToken));

    /// <summary>Returns whether a backend or device change since Jellyfin started still needs a restart.</summary>
    /// <returns>True until Jellyfin restarts.</returns>
    [HttpGet("RestartRequired")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<bool> RestartRequired() => settings.RestartRequired;

    /// <summary>Returns every change HwProbe made, oldest first.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The history.</returns>
    [HttpGet("History")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HistoryEntry>>> HistoryAsync(CancellationToken cancellationToken) =>
        Ok(await settings.HistoryAsync(cancellationToken));

    /// <summary>Maps a result to its HTTP response.</summary>
    /// <param name="result">The result.</param>
    /// <returns>200, 400 or 409.</returns>
    private ActionResult<ApplyResult> ToResponse(ApplyResult result) => result.Outcome switch
    {
        ApplyOutcome.Applied => Ok(result),
        ApplyOutcome.Rejected => BadRequest(result),
        _ => Conflict(result),
    };

    /// <summary>Returns the name of the admin making the request.</summary>
    /// <returns>The user name, or <c>unknown</c>.</returns>
    private string UserName() => User.Identity?.Name ?? "unknown";
}
