using Jellyfin.Plugin.HwProbe.Probing;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>Admin API for running probes and reading the report.</summary>
/// <param name="service">The probe runner.</param>
[ApiController]
[Route("HwProbe")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class HwProbeController(ProbeService service) : ControllerBase
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

    /// <summary>Returns whether a probe is running and how the last one ended.</summary>
    /// <returns>The status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ProbeStatus> CurrentStatus() => service.Status;

    /// <summary>Starts a probe in the background.</summary>
    /// <returns>202 when started; 409 when one is running or a session is transcoding.</returns>
    [HttpPost("Run")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult Run() => service.Start() switch
    {
        ProbeRunResult.Started => Accepted(),
        ProbeRunResult.AlreadyRunning => Conflict("A probe is already running."),
        ProbeRunResult.ServerBusy => Conflict("A session is transcoding; probe when the server is idle."),
        var other => Problem($"Unexpected result {other}."),
    };
}
