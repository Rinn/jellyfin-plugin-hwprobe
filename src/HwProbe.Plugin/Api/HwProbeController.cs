using Jellyfin.Plugin.HwProbe.Core.Diagnostics;
using Jellyfin.Plugin.HwProbe.Core.Fixtures;
using Jellyfin.Plugin.HwProbe.Core.Report;
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
        ProbeRunResult.AlreadyRunning => Conflict("A probe or performance test is already running."),
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
        ProbeRunResult.Invalid => BadRequest("Unknown method, test, or comparison."),
        ProbeRunResult.NoReport => Conflict("Run a probe first, so the performance test knows which backends work."),
        ProbeRunResult.AlreadyRunning => Conflict("A probe or performance test is already running."),
        ProbeRunResult.ServerBusy => Conflict("A session is transcoding; measure when the server is idle."),
        var other => Problem($"Unexpected result {other}."),
    };

    /// <summary>Lists the test suites as this server would run them.</summary>
    /// <param name="cancellationToken">Cancels reading the latest report.</param>
    /// <returns>Every suite, with its steps and whether it's offered.</returns>
    [HttpGet("Suites")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SuiteInfo>>> SuitesAsync(CancellationToken cancellationToken) => Ok(await service.SuitesAsync(cancellationToken));

    /// <summary>Starts a test suite: its steps run one after another.</summary>
    /// <param name="request">The suite and how to run it.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>202 when started; 400 for an unknown suite or one this server can't run; 409 when busy or no probe has run.</returns>
    [HttpPost("Suite")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> StartSuiteAsync([FromBody] SuiteRequest request, CancellationToken cancellationToken) => await service.StartSuiteAsync(request, cancellationToken) switch
    {
        ProbeRunResult.Started => Accepted(),
        ProbeRunResult.Invalid => BadRequest("Unknown suite, or one this server can't run."),
        ProbeRunResult.NoReport => Conflict("Run a probe first, so the performance test knows which backends work."),
        ProbeRunResult.AlreadyRunning => Conflict("A probe or performance test is already running."),
        ProbeRunResult.ServerBusy => Conflict("A session is transcoding; measure when the server is idle."),
        var other => Problem($"Unexpected result {other}."),
    };

    /// <summary>Pauses the running speed run when its current measurement finishes.</summary>
    /// <returns>204 when pausing; 409 when no speed run is running.</returns>
    [HttpPost("Speed/Pause")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult PauseSpeed() => service.PauseSpeed(true) ? NoContent() : Conflict("No performance test is running.");

    /// <summary>Resumes a paused speed run.</summary>
    /// <returns>204 when resuming; 409 when no speed run is running.</returns>
    [HttpPost("Speed/Resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult ResumeSpeed() => service.PauseSpeed(false) ? NoContent() : Conflict("No performance test is running.");

    /// <summary>Cancels the running speed run, keeping the measurements already finished.</summary>
    /// <returns>204 when cancelling; 409 when no speed run is running.</returns>
    [HttpPost("Speed/Cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult CancelSpeed() => service.CancelSpeed() ? NoContent() : Conflict("No performance test is running.");

    /// <summary>Returns everything the page lists: speed videos, outputs and choices, and the labels for backends and results.</summary>
    /// <returns>The catalog.</returns>
    [HttpGet("Catalog")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<CatalogInfo> Catalog() => Ok(CatalogInfo.From(Core.Data.Catalog.Default));

    /// <summary>Returns the link that opens a hardware report on GitHub with the latest probe's host, versions, and results filled in.</summary>
    /// <param name="jellyfin">The server's Jellyfin version, as the page knows it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The link; the empty form when no probe has completed.</returns>
    [HttpGet("IssueLink")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<string>> IssueLinkAsync([FromQuery] string? jellyfin, CancellationToken cancellationToken) =>
        await service.LatestJsonAsync(cancellationToken) is { } json && ReportStore.Deserialize(json) is { } report
            ? IssueLink.For(report, jellyfin)
            : IssueLink.Form;

    /// <summary>Returns the third-party libraries the plugin ships, with their licences, from the libraries.json that <c>scripts/package.py</c> puts beside the plugin's assemblies.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The list, as JSON; 404 for a build that wasn't packaged.</returns>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> LibrariesAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetDirectoryName(typeof(HwProbeController).Assembly.Location)!, "libraries.json");
        return System.IO.File.Exists(path) ? Content(await System.IO.File.ReadAllTextAsync(path, cancellationToken), "application/json") : NotFound();
    }

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

    /// <summary>Deletes one saved speed run.</summary>
    /// <param name="id">The run.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>204 when deleted; 404 when there's no such run; 409 while a probe or speed run is running.</returns>
    [HttpDelete("SpeedHistory/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteSpeedRunAsync([FromRoute] string id, CancellationToken cancellationToken) =>
        Deleted(await service.DeleteSpeedHistoryAsync(id, cancellationToken));

    /// <summary>Deletes every saved speed run.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>204 when deleted; 409 while a probe or speed run is running.</returns>
    [HttpDelete("SpeedHistory")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteSpeedHistoryAsync(CancellationToken cancellationToken) =>
        Deleted(await service.DeleteSpeedHistoryAsync(null, cancellationToken));

    /// <summary>Returns suggestions drawn from a performance test and the runs saved with this version and ffmpeg.</summary>
    /// <param name="id">The run shown, or none for the latest.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The suggestions; empty when there's no such run.</returns>
    [HttpGet("SpeedSuggestions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SpeedSuggestion>>> SpeedSuggestionsAsync([FromQuery] string? id, CancellationToken cancellationToken) =>
        Ok(await service.SpeedSuggestionsAsync(id, cancellationToken));

    /// <summary>Applies a setting a performance test suggested.</summary>
    /// <param name="change">The option and value.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>200 with what changed; 400 when it isn't suggested; 409 while a change is running.</returns>
    [HttpPost("ApplyMeasured")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplyResult>> ApplyMeasuredAsync([FromBody] MeasuredChange change, CancellationToken cancellationToken) =>
        ToResponse(await settings.ApplyMeasuredAsync(change, UserName(), cancellationToken));

    /// <summary>Returns the size of the cached test clips and samples.</summary>
    /// <returns>Bytes and files.</returns>
    [HttpGet("Cache")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<CacheSize> Cache() => service.FixtureCacheSize();

    /// <summary>Lists the cached test clips, samples and downloads.</summary>
    /// <returns>Each file with what it is.</returns>
    [HttpGet("Cache/Contents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<CacheEntry>> CacheContents() => Ok(service.FixtureCacheContents());

    /// <summary>Deletes the cached test clips and samples.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>204 when deleted; 409 while a probe or speed run uses them.</returns>
    [HttpDelete("Cache")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> PurgeCacheAsync(CancellationToken cancellationToken) =>
        await service.PurgeFixtureCacheAsync(cancellationToken) ? NoContent() : Conflict("A probe or performance test is using the cache.");

    /// <summary>Deletes one cached clip, sample or download.</summary>
    /// <param name="folder">The entry's folder, as <c>Cache/Contents</c> lists it.</param>
    /// <param name="file">The entry's file name.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>204 when deleted; 404 for a file the cache doesn't list; 409 while a probe or speed run is running.</returns>
    [HttpDelete("Cache/File")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteCacheFileAsync([FromQuery] string folder, [FromQuery] string file, CancellationToken cancellationToken) =>
        Deleted(await service.DeleteCacheFileAsync(folder, file, cancellationToken));

    /// <summary>Deletes everything HwProbe saved: reports, diagnostics, performance test runs and measurements, the settings change history, and the cache.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>204 when deleted; 409, with nothing deleted, while a probe, speed run or settings change is running, or when a file in use was left.</returns>
    [HttpDelete("Data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> PurgeAllAsync(CancellationToken cancellationToken) =>
        await service.PurgeAllAsync(settings.ClearHistoryAsync, cancellationToken)
            ? NoContent()
            : Conflict("A probe, performance test, or settings change is running, or a file was in use.");

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

    /// <summary>Maps a delete outcome to its response.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>204, 404 or 409.</returns>
    private ActionResult Deleted(DeleteOutcome outcome) => outcome switch
    {
        DeleteOutcome.Deleted => NoContent(),
        DeleteOutcome.NotFound => NotFound(),
        _ => Conflict("A probe or performance test is running."),
    };
}
