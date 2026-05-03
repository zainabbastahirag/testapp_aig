using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace ExperionAgent.API.Controllers;

[ApiController]
[Route("api/chat")]
[Produces("application/json")]
public class ChatController : ControllerBase
{
    private readonly IIdentityResolver _identity;
    private readonly IChatOrchestrator _chat;

    public ChatController(IIdentityResolver identity, IChatOrchestrator chat)
    {
        _identity = identity;
        _chat = chat;
    }

    /// <summary>
    /// Process a circle gesture: DOM fragment → context detection → KB search → AI response.
    /// </summary>
    [HttpPost("process")]
    public async Task<ActionResult<ChatResponse>> Process(
        [FromBody] CircleProcessRequest request, CancellationToken ct)
    {
        if (request.CapturedElements == null || request.CapturedElements.Count == 0)
        {
            return BadRequest(new ChatResponse
            {
                Success = false,
                ErrorMessage = "No captured elements provided."
            });
        }

        var ip = HttpContext.Items["ClientIp"]?.ToString();
        var resolved = await _identity.ResolveAsync(request.UserId, request.ClientFingerprint, ip, ct);

        var result = await _chat.ProcessCircleAsync(request, resolved.UserId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Follow-up question within an existing conversation.
    /// </summary>
    [HttpPost("ask")]
    public async Task<ActionResult<ChatResponse>> Ask(
        [FromBody] ChatAskRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new ChatResponse { Success = false, ErrorMessage = "question is required." });

        var ip = HttpContext.Items["ClientIp"]?.ToString();
        var resolved = await _identity.ResolveAsync(request.UserId, request.ClientFingerprint, ip, ct);

        var result = await _chat.AskAsync(request, resolved.UserId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Record feedback for a conversation.
    /// </summary>
    [HttpPost("feedback")]
    public async Task<ActionResult> Feedback([FromBody] FeedbackRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.ConversationId))
            return BadRequest("conversationId is required.");

        await _chat.LogFeedbackAsync(request.ConversationId, request.Positive, request.FeedbackText, ct);
        return Ok(new { success = true });
    }
}
