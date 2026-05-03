using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AGOneExperion.API.Controllers;

/// <summary>
/// Recommendation Agent endpoint.
/// Called by the SDK proactively (e.g., after N page views or after idle timeout)
/// to surface contextual product/content suggestions without a circle gesture.
/// </summary>
[ApiController]
[Route("api/recommend")]
[Produces("application/json")]
public class RecommendController : ControllerBase
{
    private readonly IExperionOrchestrator _orchestrator;
    private readonly IUserIdentityResolver _identity;
    private readonly ILogger<RecommendController> _log;

    public RecommendController(
        IExperionOrchestrator orchestrator,
        IUserIdentityResolver identity,
        ILogger<RecommendController> log)
    {
        _orchestrator = orchestrator;
        _identity = identity;
        _log = log;
    }

    /// <summary>
    /// Generate personalised recommendations for a user on the current page.
    /// The engine reads the user's full activity profile from Blob Storage
    /// and uses the LLM to produce ranked, explainable suggestions.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RecommendationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecommendationResponse>> Recommend(
        [FromBody] RecommendationRequest request, CancellationToken ct)
    {
        var ip = Request.Headers["X-Forwarded-For"].FirstOrDefault()
                 ?? HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        request.UserId = _identity.Resolve(request.UserId, ip, ua, out _);

        _log.LogInformation("[Recommend] POST — user={UserId}, page={Page}", request.UserId, request.PageUrl);

        var result = await _orchestrator.GetRecommendationsAsync(request, ct);
        return Ok(result);
    }
}
