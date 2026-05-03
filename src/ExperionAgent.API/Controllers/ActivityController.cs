using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace ExperionAgent.API.Controllers;

[ApiController]
[Route("api/activity")]
[Produces("application/json")]
public class ActivityController : ControllerBase
{
    private readonly IIdentityResolver _identity;
    private readonly IActivityMiner _miner;
    private readonly ILogger<ActivityController> _log;

    public ActivityController(
        IIdentityResolver identity,
        IActivityMiner miner,
        ILogger<ActivityController> log)
    {
        _identity = identity;
        _miner = miner;
        _log = log;
    }

    /// <summary>
    /// Ingest a batch of activity events from the SDK.
    /// </summary>
    [HttpPost("ingest")]
    public async Task<ActionResult<ActivityIngestResponse>> Ingest(
        [FromBody] ActivityIngestRequest request, CancellationToken ct)
    {
        if (request.Events == null || request.Events.Count == 0)
            return BadRequest(new ActivityIngestResponse { Success = false });

        var ip = HttpContext.Items["ClientIp"]?.ToString();
        var resolved = await _identity.ResolveAsync(request.UserId, request.ClientFingerprint, ip, ct);

        var result = await _miner.IngestAsync(request, resolved.UserId, resolved.Location, ct);
        return Ok(result);
    }
}
