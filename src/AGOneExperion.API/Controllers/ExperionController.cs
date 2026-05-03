using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AGOneExperion.API.Controllers;

/// <summary>
/// Experion AI pipeline endpoints.
/// Handles circle-gesture contextual help, follow-up chat, and user feedback.
/// </summary>
[ApiController]
[Route("api/experion")]
[Produces("application/json")]
public class ExperionController : ControllerBase
{
    private readonly IExperionOrchestrator _orchestrator;
    private readonly IUserIdentityResolver _identity;
    private readonly ILogger<ExperionController> _log;

    public ExperionController(
        IExperionOrchestrator orchestrator,
        IUserIdentityResolver identity,
        ILogger<ExperionController> log)
    {
        _orchestrator = orchestrator;
        _identity = identity;
        _log = log;
    }

    /// <summary>
    /// Full circle-gesture pipeline.
    /// DOM fragment → context detection → KB search → RAG response + recommendations.
    /// </summary>
    [HttpPost("process")]
    [ProducesResponseType(typeof(ExperionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExperionResponse>> Process(
        [FromBody] ExperionRequest request, CancellationToken ct)
    {
        if (request.CapturedElements == null || request.CapturedElements.Count == 0)
            return BadRequest(new ExperionResponse
            {
                Success = false,
                ErrorMessage = "No captured elements. Please circle an area on the page first."
            });

        var ip = GetClientIp();
        var ua = Request.Headers.UserAgent.ToString();
        request.UserId = _identity.Resolve(request.UserId, ip, ua, out _);

        _log.LogInformation("[Experion] POST /process — session={Session}, elements={Count}",
            request.SessionId, request.CapturedElements.Count);

        var result = await _orchestrator.ProcessCircleAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Follow-up question in the Experion sidebar chat.
    /// </summary>
    [HttpPost("ask")]
    [ProducesResponseType(typeof(ExperionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExperionResponse>> Ask(
        [FromBody] AskWithSessionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.SessionId)) return BadRequest("sessionId is required.");
        if (string.IsNullOrEmpty(request.Question)) return BadRequest("question is required.");

        var result = await _orchestrator.AskFollowUpAsync(
            request.SessionId,
            new ExperionAskRequest { Question = request.Question, KbIndexName = request.KbIndexName },
            ct);

        return Ok(result);
    }

    /// <summary>
    /// Record thumbs-up / thumbs-down feedback for an interaction.
    /// </summary>
    [HttpPost("feedback")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Feedback([FromBody] FeedbackRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.SessionId)) return BadRequest("sessionId is required.");

        await _orchestrator.LogFeedbackAsync(request.SessionId, request.Positive, request.FeedbackText, ct);
        return NoContent();
    }

    /// <summary>
    /// Health check endpoint used by the SDK to verify connectivity.
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Health() => Ok(new
    {
        status = "healthy",
        service = "AGOneExperion",
        version = "2.0.0",
        timestamp = DateTime.UtcNow
    });

    private string? GetClientIp() =>
        Request.Headers["X-Forwarded-For"].FirstOrDefault()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}

// Controller-specific request DTO (sessionId + question bundled together)
public class AskWithSessionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? KbIndexName { get; set; }
}
