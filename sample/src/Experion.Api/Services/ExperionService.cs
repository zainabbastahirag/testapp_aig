using System.Diagnostics;
using System.Text.Json;
using Experion.Api.Data;
using Experion.Api.Models;
using Experion.Api.Providers;
using Experion.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Experion.Api.Services;

/// <summary>
/// Single, flat service. Each pipeline step is one public method at the same level.
/// The controller calls them linearly. No nested orchestrators, no per-step classes.
///
///   Pipeline order:
///     1. EmbedQueryAsync          → returns float[] embedding
///     2. CheckSemanticCacheAsync  → returns hit/miss + cached answer
///     3. ClassifyIntentAsync      → returns ACTION or GENERATION (+ actionKey)
///     4a. RunActionAsync          → for ACTION
///     4b. RunGenerationAsync      → for GENERATION
///     5. PersistAsync             → writes blob (conversation) + SQL (cache, audit)
/// </summary>
public class ExperionService
{
    private readonly ExperionDbContext _db;
    private readonly IEmbeddingProvider _embed;
    private readonly ILlmProvider _llm;
    private readonly IBlobStore _blob;
    private readonly IActionDispatcher _actions;
    private readonly ILogger<ExperionService> _log;

    public const double CacheHitThreshold       = 0.92;
    public const double ActionEmbeddingThreshold = 0.78;
    public const double ActionPhraseThreshold    = 0.60;
    public const double ActionHybridThreshold    = 0.60;
    public const double PhraseWeight             = 0.50;

    public ExperionService(
        ExperionDbContext db,
        IEmbeddingProvider embed,
        ILlmProvider llm,
        IBlobStore blob,
        IActionDispatcher actions,
        ILogger<ExperionService> log)
    {
        _db = db; _embed = embed; _llm = llm; _blob = blob; _actions = actions; _log = log;
    }

    // ─────────────────────────────────────────────────────────────────
    // STEP 0  ·  Track Activity
    // Called by the controller's TrackActivity endpoint. Writes raw events
    // to Blob (one JSONL line per event) and updates UserProfile counters.
    // ─────────────────────────────────────────────────────────────────
    public async Task<int> TrackActivityAsync(
        string tenantId,
        string userId,
        string sessionId,
        List<ActivityEventDto> events,
        CancellationToken ct)
    {
        var blobPath = BlobPaths.Activity(tenantId, userId, DateTime.UtcNow);

        foreach (var e in events)
        {
            await _blob.AppendJsonLineAsync(BlobPaths.ActivityContainer, blobPath, new
            {
                userId,
                sessionId,
                tenantId,
                e.Type,
                e.Url,
                e.Selector,
                e.Text,
                e.Meta,
                timestamp = e.Timestamp == default ? DateTime.UtcNow : e.Timestamp
            }, ct);
        }

        var profile = await _db.UserProfiles.FindAsync(new object?[] { userId }, ct);
        if (profile == null)
        {
            profile = new UserProfile
            {
                UserId = userId,
                TenantId = tenantId,
                IsAnonymous = userId.StartsWith("anon-", StringComparison.OrdinalIgnoreCase)
            };
            _db.UserProfiles.Add(profile);
        }
        profile.TotalEvents += events.Count;
        profile.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("[Track] {Count} events for {User} -> blob://{Container}/{Path}",
            events.Count, userId, BlobPaths.ActivityContainer, blobPath);
        return events.Count;
    }

    // ─────────────────────────────────────────────────────────────────
    // STEP 1  ·  Embed the user query.
    // ─────────────────────────────────────────────────────────────────
    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken ct)
    {
        var emb = await _embed.EmbedAsync(query ?? "", ct);
        _log.LogDebug("[Process] embedded query (len={Len}, dim={Dim})", query?.Length ?? 0, emb.Length);
        return emb;
    }

    // ─────────────────────────────────────────────────────────────────
    // STEP 2  ·  Check the semantic cache. Returns null on miss.
    // ─────────────────────────────────────────────────────────────────
    public async Task<CacheLookupResult> CheckSemanticCacheAsync(
        string tenantId, float[] queryEmbedding, CancellationToken ct)
    {
        var entries = await _db.SemanticCache
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        SemanticCacheEntry? best = null;
        double bestScore = 0;
        foreach (var e in entries)
        {
            var v = JsonSerializer.Deserialize<float[]>(e.Embedding) ?? Array.Empty<float>();
            var s = VectorMath.Cosine(queryEmbedding, v);
            if (s > bestScore) { bestScore = s; best = e; }
        }

        if (best != null && bestScore >= CacheHitThreshold)
        {
            best.Hits++;
            best.LastUsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new CacheLookupResult { Hit = true, Score = bestScore, Answer = best.Answer, IntentType = best.IntentType };
        }
        return new CacheLookupResult { Hit = false, Score = bestScore };
    }

    // ─────────────────────────────────────────────────────────────────
    // STEP 3  ·  NLP intent classification → ACTION or GENERATION.
    // Hybrid: embedding cosine + phrase Jaccard against ActionMappings.
    // ─────────────────────────────────────────────────────────────────
    public async Task<IntentResult> ClassifyIntentAsync(
        string tenantId, string query, float[] queryEmbedding, CancellationToken ct)
    {
        var mappings = await _db.ActionMappings
            .Where(a => a.TenantId == tenantId && a.Enabled)
            .ToListAsync(ct);

        var qLower = (query ?? "").ToLowerInvariant();

        ActionMapping? best = null;
        double bestEmb = 0, bestPhrase = 0, bestHybrid = 0;

        foreach (var m in mappings)
        {
            double emb = 0;
            if (!string.IsNullOrEmpty(m.Embedding))
            {
                var v = JsonSerializer.Deserialize<float[]>(m.Embedding) ?? Array.Empty<float>();
                emb = VectorMath.Cosine(queryEmbedding, v);
            }

            double phrase = 0;
            foreach (var p in (m.Phrases ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var pl = p.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(pl)) continue;
                if (qLower.Contains(pl) || pl.Contains(qLower)) { phrase = Math.Max(phrase, 1.0); continue; }
                var qT = Tokens(qLower);
                var pT = Tokens(pl);
                if (qT.Count == 0 || pT.Count == 0) continue;
                var inter = qT.Intersect(pT).Count();
                var union = qT.Union(pT).Count();
                if (union > 0) phrase = Math.Max(phrase, (double)inter / union);
            }

            var hybrid = emb * (1 - PhraseWeight) + phrase * PhraseWeight;
            if (hybrid > bestHybrid) { bestHybrid = hybrid; bestEmb = emb; bestPhrase = phrase; best = m; }
        }

        bool fired = best != null && (
            bestEmb    >= ActionEmbeddingThreshold ||
            bestPhrase >= ActionPhraseThreshold    ||
            bestHybrid >= ActionHybridThreshold);

        return new IntentResult
        {
            IntentType   = fired ? "ACTION" : "GENERATION",
            ActionKey    = fired ? best!.ActionKey : null,
            EmbScore     = bestEmb,
            PhraseScore  = bestPhrase,
            HybridScore  = bestHybrid
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // STEP 4a  ·  Action path: dispatch the action job.
    // ─────────────────────────────────────────────────────────────────
    public async Task<AnswerResult> RunActionAsync(
        ProcessRequest request, string userId, string actionKey, CancellationToken ct)
    {
        await _actions.DispatchAsync(new ActionJob
        {
            ActionKey = actionKey,
            UserId    = userId,
            SessionId = request.SessionId,
            PageUrl   = request.PageUrl,
            Query     = request.UserMessage
        }, ct);

        return new AnswerResult
        {
            Message = $"✓ I'll **{actionKey.Replace('_', ' ')}** for you. (Action dispatched to action-queue.)",
            Suggestions = new List<string> { "Show me my cart", "Continue browsing", "Cancel" }
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // STEP 4b  ·  Generation path: KB retrieval + LLM.
    // ─────────────────────────────────────────────────────────────────
    public async Task<AnswerResult> RunGenerationAsync(
        string tenantId, string sessionId, string userId, string query, CancellationToken ct)
    {
        // Pull the recent conversation history (last 6 turns) directly from blob.
        var history = await ReadConversationTailAsync(tenantId, userId, 6, ct);
        var historyBlock = string.Join("\n",
            history.Select(h => $"{h.Role}: {h.Content}"));

        // Naive KB retrieval against tenant KB content.
        var tenant = await _db.Tenants.FindAsync(new object?[] { tenantId }, ct);
        var kb = tenant?.KbContent ?? "";
        var sections = kb.Split("\n\n## ", StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.StartsWith("##") ? s : "## " + s).ToList();

        var qTokens = Tokens(query.ToLowerInvariant());
        var topSections = sections
            .Select(sec => new { Section = sec, Score = Tokens(sec.ToLowerInvariant()).Count(t => qTokens.Contains(t)) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => x.Section)
            .ToList();

        var kbBlock = string.Join("\n\n", topSections);

        var systemPrompt = "You are Experion, a helpful AI assistant. Answer using the CONTEXT below. " +
                           "If the context doesn't cover the question, say so honestly. Be concise.";
        var userPrompt =
            (string.IsNullOrEmpty(historyBlock) ? "" : $"## HISTORY\n{historyBlock}\n\n") +
            (string.IsNullOrEmpty(kbBlock)      ? "" : $"## CONTEXT\n{kbBlock}\n\n") +
            $"## QUESTION\n{query}";

        var answer = await _llm.CompleteAsync(systemPrompt, userPrompt, ct);

        var suggestions = topSections
            .Select(s => System.Text.RegularExpressions.Regex.Match(s, @"^##\s*(.+)$",
                System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => $"Tell me more about {s}")
            .Take(3)
            .ToList();

        return new AnswerResult { Message = answer, Suggestions = suggestions, KbSectionsUsed = topSections.Count };
    }

    // ─────────────────────────────────────────────────────────────────
    // STEP 5  ·  Persist conversation (Blob), cache write-back (SQL),
    //            and audit row (SQL).
    // ─────────────────────────────────────────────────────────────────
    public async Task PersistAsync(
        ProcessRequest request, string userId, string logId,
        string intentType, string? actionKey,
        bool cacheHit, float[] queryEmbedding,
        AnswerResult answer, long elapsedMs,
        CancellationToken ct)
    {
        // 5a. Blob — conversation history (one JSONL line per turn)
        var convPath = BlobPaths.Conversation(request.TenantId, userId, DateTime.UtcNow);
        await _blob.AppendJsonLineAsync(BlobPaths.ConversationsContainer, convPath, new
        {
            ts = DateTime.UtcNow, sessionId = request.SessionId, userId,
            role = "user", content = request.UserMessage, intentType, actionKey, cacheHit
        }, ct);
        await _blob.AppendJsonLineAsync(BlobPaths.ConversationsContainer, convPath, new
        {
            ts = DateTime.UtcNow, sessionId = request.SessionId, userId,
            role = "assistant", content = answer.Message, intentType, actionKey, cacheHit
        }, ct);

        // 5b. Cache write-back — only on miss + GENERATION + non-empty answer
        if (!cacheHit && intentType == "GENERATION" && !string.IsNullOrEmpty(answer.Message))
        {
            _db.SemanticCache.Add(new SemanticCacheEntry
            {
                TenantId = request.TenantId,
                NormalisedQuery = request.UserMessage,
                Embedding = JsonSerializer.Serialize(queryEmbedding),
                Answer = answer.Message,
                IntentType = intentType
            });
        }

        // 5c. Audit log
        _db.AuditLogs.Add(new AuditLog
        {
            LogId = logId,
            SessionId = request.SessionId,
            UserId = userId,
            IntentType = intentType,
            ActionKey = actionKey,
            CacheHit = cacheHit,
            Prompt = request.UserMessage,
            Response = answer.Message,
            ElapsedMs = elapsedMs
        });

        await _db.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helper — used by RunGenerationAsync and inspectors.
    // Reads the last N JSONL lines of a user's conversation log from blob.
    // ─────────────────────────────────────────────────────────────────
    public async Task<List<ConversationTurnRecord>> ReadConversationTailAsync(
        string tenantId, string userId, int n, CancellationToken ct)
    {
        var path = BlobPaths.Conversation(tenantId, userId, DateTime.UtcNow);
        var lines = await _blob.ReadTailAsync(BlobPaths.ConversationsContainer, path, n, ct);
        var rows = new List<ConversationTurnRecord>();
        foreach (var line in lines)
        {
            try
            {
                var d = JsonDocument.Parse(line);
                rows.Add(new ConversationTurnRecord
                {
                    Ts = d.RootElement.GetProperty("ts").GetDateTime(),
                    Role = d.RootElement.GetProperty("role").GetString() ?? "user",
                    Content = d.RootElement.GetProperty("content").GetString() ?? ""
                });
            }
            catch { /* skip bad line */ }
        }
        return rows;
    }

    public async Task<List<string>> ListUserBlobsAsync(string tenantId, string userId, CancellationToken ct)
    {
        var conv = await _blob.ListAsync(BlobPaths.ConversationsContainer, BlobPaths.UserPrefix(tenantId, userId), ct);
        var act  = await _blob.ListAsync(BlobPaths.ActivityContainer,      BlobPaths.UserPrefix(tenantId, userId), ct);
        return conv.Select(p => $"conversations/{p}").Concat(act.Select(p => $"activity/{p}")).ToList();
    }

    public async Task<List<string>> ReadBlobLinesAsync(string container, string path, int n, CancellationToken ct) =>
        await _blob.ReadTailAsync(container, path, n, ct);

    // ── Small helpers ────────────────────────────────────────────────
    private static HashSet<string> Tokens(string s) =>
        System.Text.RegularExpressions.Regex.Matches(s, @"[a-z0-9]+")
            .Select(m => m.Value).Where(t => t.Length > 2).ToHashSet();
}

// ── Tiny DTOs returned by each step (kept here so the service stays self-contained) ──

public class CacheLookupResult
{
    public bool Hit { get; set; }
    public double Score { get; set; }
    public string? Answer { get; set; }
    public string? IntentType { get; set; }
}

public class IntentResult
{
    public string IntentType { get; set; } = "GENERATION";
    public string? ActionKey { get; set; }
    public double EmbScore { get; set; }
    public double PhraseScore { get; set; }
    public double HybridScore { get; set; }
}

public class AnswerResult
{
    public string Message { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = new();
    public int KbSectionsUsed { get; set; }
}

public class ConversationTurnRecord
{
    public DateTime Ts { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}
