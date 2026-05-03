using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AGOneExperion.API.Controllers;

/// <summary>
/// Activity Mining endpoint.
/// The SDK calls this on every meaningful user event (scroll, click, page view, etc.).
/// No authentication required — user identity is resolved from UserId or IP fingerprint.
/// </summary>
[ApiController]
[Route("api/activity")]
[Produces("application/json")]
public class ActivityController : ControllerBase
{
    private readonly IExperionOrchestrator _orchestrator;
    private readonly IUserIdentityResolver _identity;
    private readonly ILogger<ActivityController> _log;

    public ActivityController(
        IExperionOrchestrator orchestrator,
        IUserIdentityResolver identity,
        ILogger<ActivityController> log)
    {
        _orchestrator = orchestrator;
        _identity = identity;
        _log = log;
    }

    /// <summary>
    /// Ingest a batch of activity events from the SDK.
    /// Accepts up to 50 events per call. Returns 204 on success (fire-and-forget friendly).
    /// </summary>
    [HttpPost("track")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Track([FromBody] ActivityBatchRequest request, CancellationToken ct)
    {
        if (request.Events == null || request.Events.Count == 0)
            return BadRequest("No events provided.");

        if (request.Events.Count > 50)
            return BadRequest("Max 50 events per batch.");

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                        ?? Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var userAgent = Request.Headers.UserAgent.ToString();

        // Resolve user identity (authenticated or anonymous via IP fingerprint)
        request.UserId = _identity.Resolve(request.UserId, ipAddress, userAgent, out _);

        await _orchestrator.TrackActivityAsync(request, ipAddress, userAgent, ct);

        _log.LogDebug("[Activity] Tracked {Count} events for user {UserId}", request.Events.Count, request.UserId);
        return NoContent();
    }
}
