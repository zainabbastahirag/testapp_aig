using Experion.Api.Data;
using Experion.Api.Models;
using Experion.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Experion.Api.Controllers;

[ApiController]
[Route("api/experion")]
[Produces("application/json")]
public class ExperionController : ControllerBase
{
    private readonly IExperionService _service;
    private readonly IActivityEventBus _bus;
    private readonly ExperionDbContext _db;

    public ExperionController(IExperionService service, IActivityEventBus bus, ExperionDbContext db)
    {
        _service = service;
        _bus = bus;
        _db = db;
    }

    /// <summary>Health-check.</summary>
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { ok = true, ts = DateTime.UtcNow });

    /// <summary>Resolve / mint identity. Returns a sessionId that the SDK uses for everything else.</summary>
    [HttpPost("identify")]
    public async Task<ActionResult<IdentifyResponse>> Identify([FromBody] IdentifyRequest req, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var region = ip.StartsWith("127.") || ip == "::1" ? "local-dev" : $"ip-{ip[..Math.Min(ip.Length, 7)]}";

        string userId;
        bool anon;
        if (!string.IsNullOrEmpty(req.UserId))
        {
            userId = req.UserId;
            anon = false;
        }
        else if (!string.IsNullOrEmpty(req.AnonId))
        {
            userId = req.AnonId.StartsWith("anon-") ? req.AnonId : $"anon-{req.AnonId}";
            anon = true;
        }
        else
        {
            userId = $"anon-{Guid.NewGuid().ToString("N")[..10]}";
            anon = true;
        }

        var sessionId = $"{userId}__{Guid.NewGuid().ToString("N")[..8]}";

        // Upsert profile
        var profile = await _db.UserProfiles.FindAsync(new object?[] { userId }, ct);
        if (profile == null)
        {
            profile = new UserProfile { UserId = userId, IsAnonymous = anon, TenantId = req.TenantId, RegionCluster = region };
            _db.UserProfiles.Add(profile);
        }
        profile.RegionCluster ??= region;
        profile.LastSeenAt = DateTime.UtcNow;
        profile.TotalSessions += 1;
        await _db.SaveChangesAsync(ct);

        return Ok(new IdentifyResponse
        {
            SessionId = sessionId,
            ResolvedUserId = userId,
            IsAnonymous = anon,
            RegionCluster = region
        });
    }

    /// <summary>Tenant config for the SDK boot (greeting, nudge thresholds).</summary>
    [HttpGet("config/{tenantId}")]
    public async Task<IActionResult> GetConfig(string tenantId, CancellationToken ct)
    {
        var t = await _db.Tenants.FindAsync(new object?[] { tenantId }, ct);
        if (t == null) return NotFound(new { error = "tenant not found" });
        return Ok(new
        {
            tenantId = t.TenantId,
            name = t.Name,
            greeting = t.GreetingMessage,
            idleNudgeSeconds = t.IdleNudgeSeconds,
            nudgeCooldownSeconds = t.NudgeCooldownSeconds
        });
    }

    /// <summary>Async ingestion of a batch of activity events.</summary>
    [HttpPost("events")]
    public async Task<IActionResult> Events([FromBody] EventBatchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.SessionId)) return BadRequest(new { error = "sessionId required" });
        foreach (var e in req.Events)
            await _bus.PublishAsync(req.SessionId, e, ct);
        return Accepted(new { received = req.Events.Count });
    }

    /// <summary>Synchronous chat / circle / idle entry point (the orchestrator pipeline).</summary>
    [HttpPost("process")]
    public async Task<ActionResult<ProcessResponse>> Process([FromBody] ProcessRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.SessionId)) return BadRequest(new { error = "sessionId required" });
        if (string.IsNullOrEmpty(req.UserMessage) && string.IsNullOrEmpty(req.CapturedText))
            return BadRequest(new { error = "userMessage or capturedText required" });

        var result = await _service.ProcessAsync(req, ct);
        return Ok(result);
    }

    /// <summary>Record thumbs up/down feedback for a previous answer.</summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] FeedbackRequest req, CancellationToken ct)
    {
        await _service.RecordFeedbackAsync(req, ct);
        return Ok(new { success = true });
    }

    // ── Inspection endpoints (handy for the demo page) ──────────────

    [HttpGet("inspect/history/{sessionId}")]
    public async Task<IActionResult> History(string sessionId, CancellationToken ct)
    {
        var rows = await _db.ConversationHistory
            .Where(c => c.SessionId == sessionId)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("inspect/cache")]
    public async Task<IActionResult> Cache(CancellationToken ct)
    {
        var rows = await _db.SemanticCache.OrderByDescending(c => c.LastUsedAt).Take(50).ToListAsync(ct);
        return Ok(rows.Select(r => new
        {
            r.Id,
            r.NormalisedQuery,
            r.Answer,
            r.IntentType,
            r.Hits,
            r.CreatedAt,
            r.LastUsedAt
        }));
    }

    [HttpGet("inspect/audit")]
    public async Task<IActionResult> Audit(CancellationToken ct)
    {
        var rows = await _db.AuditLogs.OrderByDescending(a => a.Id).Take(50).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("inspect/profile/{userId}")]
    public async Task<IActionResult> Profile(string userId, CancellationToken ct)
    {
        var p = await _db.UserProfiles.FindAsync(new object?[] { userId }, ct);
        if (p == null) return NotFound();
        var recent = await _db.ActivityEvents
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Id)
            .Take(20)
            .ToListAsync(ct);
        return Ok(new { profile = p, recentEvents = recent });
    }

    [HttpGet("inspect/actions")]
    public async Task<IActionResult> Actions(CancellationToken ct)
    {
        var rows = await _db.ActionMappings
            .Select(a => new { a.Id, a.TenantId, a.ActionKey, a.Description, a.Phrases, a.Enabled })
            .ToListAsync(ct);
        return Ok(rows);
    }
}
