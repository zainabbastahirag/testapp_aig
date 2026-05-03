using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace ExperionAgent.API.Controllers;

[ApiController]
[Route("api/recommend")]
[Produces("application/json")]
public class RecommendController : ControllerBase
{
    private readonly IIdentityResolver _identity;
    private readonly IRecommendationEngine _engine;

    public RecommendController(IIdentityResolver identity, IRecommendationEngine engine)
    {
        _identity = identity;
        _engine = engine;
    }

    /// <summary>
    /// Generate proactive recommendations based on user activity profile.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<RecommendationResponse>> Recommend(
        [FromBody] RecommendationRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Items["ClientIp"]?.ToString();
        var resolved = await _identity.ResolveAsync(request.UserId, request.ClientFingerprint, ip, ct);

        var result = await _engine.GenerateAsync(request, resolved.UserId, ct);
        return Ok(result);
    }
}
