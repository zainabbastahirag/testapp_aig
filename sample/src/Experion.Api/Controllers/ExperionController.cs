using System.Diagnostics;
using Experion.Api.Data;
using Experion.Api.Models;
using Experion.Api.Services;
using Experion.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Experion.Api.Controllers;

/// <summary>
/// Experion controller — exposes TWO main endpoints:
///   ① POST /track    — track activity (passive observation)
///   ② POST /process  — process a chat / action / generation request
///
/// Both endpoints are flat — they call the ExperionService steps in order.
/// No nested orchestrators, no per-step classes — every step is a single
/// public method on ExperionService.
/// </summary>
[ApiController]
[Route("api/experion")]
[Produces("application/json")]
public class ExperionController : ControllerBase
{
    private readonly ExperionService _service;
    private readonly RecommendationEngine _reco;
    private readonly ExperionDbContext _db;

    public ExperionController(ExperionService service, RecommendationEngine reco, ExperionDbContext db)
    {
        _service = service;
        _reco = reco;
        _db = db;
    }

    // ═══════════════════════════════════════════════════════════════════
    // FUNCTION 1  ·  Track Activity
    //   - Writes raw events to blob (JSONL)
    //   - Updates UserProfile counters
    //   - Triggers RecommendationEngine to evaluate (may push a nudge)
    // ═══════════════════════════════════════════════════════════════════
    [HttpPost("track")]
    public async Task<ActionResult<TrackResponse>> TrackActivity(
        [FromBody] TrackActivityRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.SessionId)) return BadRequest(new { error = "sessionId required" });
        if (request.Events == null || request.Events.Count == 0) return BadRequest(new { error = "events required" });

        var userId = ResolveUserId(request.SessionId);

        var written = await _service.TrackActivityAsync(
            request.TenantId, userId, request.SessionId, request.Events, ct);

        await _reco.EvaluateAsync(request.TenantId, userId, ct);

        return Ok(new TrackResponse
        {
            Received = written,
            BlobPath = $"{BlobPaths.ActivityContainer}/{BlobPaths.Activity(request.TenantId, userId, DateTime.UtcNow)}"
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // FUNCTION 2  ·  Process (chat / action / generation)
    //
    // Linear flow — exactly the pipeline shown in the architecture diagram:
    //   ① Embed query
    //   ② Check semantic cache              (HIT → short-circuit)
    //   ③ Classify intent (NLP router)      → ACTION or GENERATION
    //   ④a Run action  OR  ④b Run generation
    //   ⑤ Persist (conversation → blob, cache + audit → SQL)
    // ═══════════════════════════════════════════════════════════════════
    [HttpPost("process")]
    public async Task<ActionResult<ProcessResponse>> Process(
        [FromBody] ProcessRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.SessionId)) return BadRequest(new { error = "sessionId required" });
        if (string.IsNullOrEmpty(request.UserMessage) && string.IsNullOrEmpty(request.CapturedText))
            return BadRequest(new { error = "userMessage or capturedText required" });

        var sw = Stopwatch.StartNew();
        var logId = Guid.NewGuid().ToString("N");
        var userId = ResolveUserId(request.SessionId);
        var query  = BuildQuery(request);
        var pipeline = new List<PipelineStep>();

        // ── Step 1: embed the query ──────────────────────────────────
        var stepSw = Stopwatch.StartNew();
        var embedding = await _service.EmbedQueryAsync(query, ct);
        pipeline.Add(new PipelineStep { Name = "1. Embed Query", ElapsedMs = stepSw.ElapsedMilliseconds,
            Detail = $"dim={embedding.Length}" });

        // ── Step 2: semantic cache lookup ────────────────────────────
        stepSw.Restart();
        var cache = await _service.CheckSemanticCacheAsync(request.TenantId, embedding, ct);
        pipeline.Add(new PipelineStep { Name = "2. Semantic Cache", ElapsedMs = stepSw.ElapsedMilliseconds,
            Detail = cache.Hit ? $"HIT (cosine={cache.Score:F3})" : $"MISS (best cosine={cache.Score:F3})" });

        AnswerResult answer;
        string intentType;
        string? actionKey = null;

        if (cache.Hit)
        {
            // Short-circuit: reuse cached answer, skip steps 3 & 4.
            intentType = cache.IntentType ?? "GENERATION";
            answer = new AnswerResult
            {
                Message = cache.Answer ?? "",
                Suggestions = new List<string> { "Tell me more", "Show related", "Talk to a human" }
            };
        }
        else
        {
            // ── Step 3: classify intent ─────────────────────────────
            stepSw.Restart();
            var intent = await _service.ClassifyIntentAsync(request.TenantId, query, embedding, ct);
            intentType = intent.IntentType;
            actionKey = intent.ActionKey;
            pipeline.Add(new PipelineStep { Name = "3. NLP Intent Router", ElapsedMs = stepSw.ElapsedMilliseconds,
                Detail = $"intent={intentType}" + (actionKey != null ? $", action={actionKey}" : "") +
                         $" (emb={intent.EmbScore:F2}, phrase={intent.PhraseScore:F2}, hybrid={intent.HybridScore:F2})" });

            // ── Step 4a / 4b: run the chosen path ───────────────────
            stepSw.Restart();
            if (intentType == "ACTION")
            {
                answer = await _service.RunActionAsync(request, userId, actionKey!, ct);
                pipeline.Add(new PipelineStep { Name = "4a. Run Action", ElapsedMs = stepSw.ElapsedMilliseconds,
                    Detail = $"dispatched action='{actionKey}' to action-queue" });
            }
            else
            {
                answer = await _service.RunGenerationAsync(request.TenantId, request.SessionId, userId, query, ct);
                pipeline.Add(new PipelineStep { Name = "4b. Run Generation", ElapsedMs = stepSw.ElapsedMilliseconds,
                    Detail = $"kb-sections={answer.KbSectionsUsed}, answer-len={answer.Message.Length}" });
            }
        }

        sw.Stop();

        // ── Step 5: persist ─────────────────────────────────────────
        stepSw.Restart();
        await _service.PersistAsync(request, userId, logId, intentType, actionKey,
            cache.Hit, embedding, answer, sw.ElapsedMilliseconds, ct);
        pipeline.Add(new PipelineStep { Name = "5. Persist", ElapsedMs = stepSw.ElapsedMilliseconds,
            Detail = cache.Hit ? "blob: conv (2 lines), sql: audit" : "blob: conv (2 lines), sql: audit + cache" });

        return Ok(new ProcessResponse
        {
            Success = true,
            LogId = logId,
            IntentType = intentType,
            ActionKey = actionKey,
            Message = answer.Message,
            Suggestions = answer.Suggestions,
            CacheHit = cache.Hit,
            ProcessingMs = sw.ElapsedMilliseconds,
            Pipeline = pipeline
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Supporting endpoints — identity, config, feedback, inspectors.
    // (These are thin and unchanged in spirit; the two big endpoints
    //  above are the ones in the architecture diagram.)
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { ok = true, ts = DateTime.UtcNow });

    [HttpPost("identify")]
    public async Task<ActionResult<IdentifyResponse>> Identify([FromBody] IdentifyRequest req, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var region = ip.StartsWith("127.") || ip == "::1" ? "local-dev" : $"ip-{ip[..Math.Min(ip.Length, 7)]}";

        string userId; bool anon;
        if (!string.IsNullOrEmpty(req.UserId))      { userId = req.UserId; anon = false; }
        else if (!string.IsNullOrEmpty(req.AnonId)) { userId = req.AnonId.StartsWith("anon-") ? req.AnonId : $"anon-{req.AnonId}"; anon = true; }
        else                                         { userId = $"anon-{Guid.NewGuid().ToString("N")[..10]}"; anon = true; }

        var sessionId = $"{userId}__{Guid.NewGuid().ToString("N")[..8]}";

        var profile = await _db.UserProfiles.FindAsync(new object?[] { userId }, ct);
        if (profile == null)
        {
            profile = new UserProfile { UserId = userId, IsAnonymous = anon, TenantId = req.TenantId, RegionCluster = region };
            _db.UserProfiles.Add(profile);
        }
        profile.LastSeenAt = DateTime.UtcNow;
        profile.TotalSessions += 1;
        await _db.SaveChangesAsync(ct);

        return Ok(new IdentifyResponse
        {
            SessionId = sessionId, ResolvedUserId = userId, IsAnonymous = anon, RegionCluster = region
        });
    }

    [HttpGet("config/{tenantId}")]
    public async Task<IActionResult> GetConfig(string tenantId, CancellationToken ct)
    {
        var t = await _db.Tenants.FindAsync(new object?[] { tenantId }, ct);
        if (t == null) return NotFound(new { error = "tenant not found" });
        return Ok(new
        {
            tenantId = t.TenantId, name = t.Name, greeting = t.GreetingMessage,
            idleNudgeSeconds = t.IdleNudgeSeconds, nudgeCooldownSeconds = t.NudgeCooldownSeconds
        });
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] FeedbackRequest req, CancellationToken ct)
    {
        var entry = await _db.AuditLogs.FirstOrDefaultAsync(a => a.LogId == req.LogId, ct);
        if (entry != null)
        {
            entry.FeedbackPositive = req.Positive;
            entry.FeedbackComment = req.Comment;
            await _db.SaveChangesAsync(ct);
        }
        return Ok(new { success = true });
    }

    // ── Inspectors (used by the demo page to peek at storage) ──────────

    [HttpGet("inspect/conversation/{userId}")]
    public async Task<IActionResult> Conversation(string userId, [FromQuery] string tenantId = "default",
        [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var rows = await _service.ReadConversationTailAsync(tenantId, userId, take, ct);
        return Ok(rows);
    }

    [HttpGet("inspect/blobs/{userId}")]
    public async Task<IActionResult> ListBlobs(string userId, [FromQuery] string tenantId = "default",
        CancellationToken ct = default)
    {
        var paths = await _service.ListUserBlobsAsync(tenantId, userId, ct);
        return Ok(paths);
    }

    [HttpGet("inspect/blob")]
    public async Task<IActionResult> ReadBlob(
        [FromQuery] string container, [FromQuery] string path, [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        var lines = await _service.ReadBlobLinesAsync(container, path, take, ct);
        return Ok(lines.Select(l => { try { return System.Text.Json.JsonDocument.Parse(l).RootElement; }
            catch { return System.Text.Json.JsonDocument.Parse("{}").RootElement; } }));
    }

    [HttpGet("inspect/cache")]
    public async Task<IActionResult> Cache(CancellationToken ct)
    {
        var rows = await _db.SemanticCache.OrderByDescending(c => c.LastUsedAt).Take(50).ToListAsync(ct);
        return Ok(rows.Select(r => new { r.Id, r.NormalisedQuery, r.Answer, r.IntentType, r.Hits, r.CreatedAt, r.LastUsedAt }));
    }

    [HttpGet("inspect/audit")]
    public async Task<IActionResult> Audit(CancellationToken ct) =>
        Ok(await _db.AuditLogs.OrderByDescending(a => a.Id).Take(50).ToListAsync(ct));

    [HttpGet("inspect/profile/{userId}")]
    public async Task<IActionResult> Profile(string userId, CancellationToken ct)
    {
        var p = await _db.UserProfiles.FindAsync(new object?[] { userId }, ct);
        if (p == null) return NotFound();
        return Ok(p);
    }

    [HttpGet("inspect/actions")]
    public async Task<IActionResult> Actions(CancellationToken ct) =>
        Ok(await _db.ActionMappings
            .Select(a => new { a.Id, a.TenantId, a.ActionKey, a.Description, a.Phrases, a.Enabled })
            .ToListAsync(ct));

    // ── Helpers ────────────────────────────────────────────────────────

    private static string ResolveUserId(string sessionId) =>
        sessionId.Contains("__") ? sessionId.Split("__")[0] : sessionId;

    private static string BuildQuery(ProcessRequest r)
    {
        var msg = (r.UserMessage ?? "").Trim();
        var captured = (r.CapturedText ?? "").Trim();
        if (string.IsNullOrEmpty(captured)) return msg;
        return $"{msg}\n[circled-content]: {(captured.Length > 600 ? captured[..600] + "…" : captured)}";
    }
}

// ── Request/response specific to this controller ──────────────────────

public class TrackActivityRequest
{
    public string TenantId { get; set; } = "default";
    public string SessionId { get; set; } = string.Empty;
    public List<ActivityEventDto> Events { get; set; } = new();
}

public class TrackResponse
{
    public int Received { get; set; }
    public string BlobPath { get; set; } = string.Empty;
}
