using System.Text.Json;
using Experion.Api.Data;
using Experion.Api.Models;
using Experion.Api.Providers;
using Microsoft.EntityFrameworkCore;

namespace Experion.Api.Services;

// ── Shared context object passed through the pipeline ────────────────
public class PipelineContext
{
    public ProcessRequest Request { get; init; } = default!;
    public string LogId { get; init; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public string NormalisedQuery { get; set; } = string.Empty;
    public string ConversationContext { get; set; } = string.Empty;
    public string KbContext { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public bool CacheHit { get; set; }
    public string? CachedAnswer { get; set; }
    public string IntentType { get; set; } = string.Empty;     // ACTION | GENERATION
    public string? ActionKey { get; set; }
    public string FinalAnswer { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = new();
    public List<PipelineStep> Steps { get; } = new();
}

// ── 1. Context Builder ───────────────────────────────────────────────
public class ContextBuilderStep
{
    private readonly ExperionDbContext _db;
    public ContextBuilderStep(ExperionDbContext db) { _db = db; }

    public async Task RunAsync(PipelineContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Resolve user from session via UserProfile last seen — for the demo we resolve from request session.
        // The session-id format is "<user-or-anon>__<random>"; first segment is the userId.
        var sid = ctx.Request.SessionId ?? "";
        var userPart = sid.Contains("__") ? sid.Split("__")[0] : sid;
        ctx.UserId = string.IsNullOrEmpty(userPart) ? "anon-unknown" : userPart;
        ctx.IsAnonymous = ctx.UserId.StartsWith("anon-", StringComparison.OrdinalIgnoreCase);

        // Conversation history (last 6 turns)
        var history = await _db.ConversationHistory
            .Where(c => c.SessionId == ctx.Request.SessionId)
            .OrderByDescending(c => c.Id)
            .Take(6)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
        ctx.ConversationContext = string.Join("\n", history.Select(h => $"{h.Role}: {h.Content}"));

        // Normalise the user message + any captured DOM text
        var msg = (ctx.Request.UserMessage ?? "").Trim();
        var captured = (ctx.Request.CapturedText ?? "").Trim();
        ctx.NormalisedQuery = string.IsNullOrEmpty(captured)
            ? msg
            : $"{msg}\n[circled-content]: {Truncate(captured, 600)}";

        sw.Stop();
        ctx.Steps.Add(new PipelineStep
        {
            Name = "1. Context Builder",
            Detail = $"user={ctx.UserId} (anon={ctx.IsAnonymous}), history-turns={history.Count}, normalised-len={ctx.NormalisedQuery.Length}",
            ElapsedMs = sw.ElapsedMilliseconds
        });
    }

    private static string Truncate(string s, int n) => s.Length > n ? s[..n] + "…" : s;
}

// ── 2. Semantic Cache ────────────────────────────────────────────────
public class SemanticCacheStep
{
    private readonly ExperionDbContext _db;
    private readonly IEmbeddingProvider _embed;
    public const double HitThreshold = 0.92;

    public SemanticCacheStep(ExperionDbContext db, IEmbeddingProvider embed) { _db = db; _embed = embed; }

    public async Task RunAsync(PipelineContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        ctx.Embedding = await _embed.EmbedAsync(ctx.NormalisedQuery, ct);

        var entries = await _db.SemanticCache
            .Where(c => c.TenantId == ctx.Request.TenantId)
            .ToListAsync(ct);

        SemanticCacheEntry? best = null;
        double bestScore = 0;
        foreach (var e in entries)
        {
            var v = JsonSerializer.Deserialize<float[]>(e.Embedding) ?? Array.Empty<float>();
            var s = VectorMath.Cosine(ctx.Embedding, v);
            if (s > bestScore) { bestScore = s; best = e; }
        }

        if (best != null && bestScore >= HitThreshold)
        {
            ctx.CacheHit = true;
            ctx.CachedAnswer = best.Answer;
            ctx.IntentType = best.IntentType ?? "GENERATION";
            best.Hits++;
            best.LastUsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        sw.Stop();
        ctx.Steps.Add(new PipelineStep
        {
            Name = "2. Semantic Cache",
            Detail = ctx.CacheHit
                ? $"HIT (cosine={bestScore:F3}) — short-circuiting LLM"
                : $"MISS (best cosine={bestScore:F3}, threshold={HitThreshold})",
            ElapsedMs = sw.ElapsedMilliseconds
        });
    }
}

// ── 3. NLP Intent Router ─────────────────────────────────────────────
// Combines two signals (just like a real system):
//   a) Embedding cosine similarity vs. ActionMappings.Embedding
//   b) Phrase substring match vs. ActionMappings.Phrases
// Either signal above its threshold (or a combined score above a hybrid bar)
// classifies the request as ACTION.
public class IntentRouterStep
{
    private readonly ExperionDbContext _db;
    public const double EmbeddingThreshold = 0.78;
    public const double HybridThreshold    = 0.60;
    public const double PhraseWeight       = 0.50;

    public IntentRouterStep(ExperionDbContext db) { _db = db; }

    public async Task RunAsync(PipelineContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var mappings = await _db.ActionMappings
            .Where(a => a.TenantId == ctx.Request.TenantId && a.Enabled)
            .ToListAsync(ct);

        var query = ctx.NormalisedQuery.ToLowerInvariant();

        ActionMapping? best = null;
        double bestEmb = 0, bestPhrase = 0, bestHybrid = 0;
        string? bestSignal = null;

        foreach (var m in mappings)
        {
            // Embedding signal
            double emb = 0;
            if (!string.IsNullOrEmpty(m.Embedding))
            {
                var v = JsonSerializer.Deserialize<float[]>(m.Embedding) ?? Array.Empty<float>();
                emb = VectorMath.Cosine(ctx.Embedding, v);
            }

            // Phrase signal: best Jaccard between query and any of the mapping's example phrases.
            double phrase = 0;
            foreach (var p in (m.Phrases ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var pl = p.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(pl)) continue;

                if (query.Contains(pl) || pl.Contains(query))
                {
                    phrase = Math.Max(phrase, 1.0);
                    continue;
                }

                var qTokens = Tokens(query);
                var pTokens = Tokens(pl);
                if (qTokens.Count == 0 || pTokens.Count == 0) continue;
                var inter = qTokens.Intersect(pTokens).Count();
                var union = qTokens.Union(pTokens).Count();
                if (union > 0) phrase = Math.Max(phrase, (double)inter / union);
            }

            // Hybrid weighted score
            var hybrid = emb * (1 - PhraseWeight) + phrase * PhraseWeight;

            if (hybrid > bestHybrid)
            {
                bestHybrid = hybrid;
                bestEmb = emb;
                bestPhrase = phrase;
                best = m;
            }
        }

        bool fired = best != null && (
            bestEmb    >= EmbeddingThreshold ||
            bestPhrase >= 0.60 ||
            bestHybrid >= HybridThreshold);

        if (fired)
        {
            ctx.IntentType = "ACTION";
            ctx.ActionKey = best!.ActionKey;
            bestSignal = bestPhrase >= bestEmb ? "phrase" : "embedding";
        }
        else
        {
            ctx.IntentType = "GENERATION";
        }

        sw.Stop();
        ctx.Steps.Add(new PipelineStep
        {
            Name = "3. NLP Intent Router",
            Detail = $"classified={ctx.IntentType} " +
                     $"(best={best?.ActionKey ?? "-"}, emb={bestEmb:F3}, phrase={bestPhrase:F3}, hybrid={bestHybrid:F3}" +
                     (fired ? $", signal={bestSignal}" : "") + ")",
            ElapsedMs = sw.ElapsedMilliseconds
        });
    }

    private static HashSet<string> Tokens(string s) =>
        System.Text.RegularExpressions.Regex.Matches(s, @"[a-z0-9]+")
            .Select(m => m.Value).Where(t => t.Length > 2).ToHashSet();
}

// ── 4a. Action Trigger ───────────────────────────────────────────────
public class ActionTriggerStep
{
    private readonly IActionDispatcher _dispatcher;
    public ActionTriggerStep(IActionDispatcher dispatcher) { _dispatcher = dispatcher; }

    public async Task RunAsync(PipelineContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Dispatch to the action queue (in-memory channel acting as Service Bus).
        await _dispatcher.DispatchAsync(new ActionJob
        {
            ActionKey = ctx.ActionKey!,
            UserId = ctx.UserId,
            SessionId = ctx.Request.SessionId,
            PageUrl = ctx.Request.PageUrl,
            Query = ctx.NormalisedQuery
        }, ct);

        ctx.FinalAnswer = $"✓ I'll **{Humanise(ctx.ActionKey!)}** for you. (Action dispatched to action-queue.)";
        ctx.Suggestions = new List<string> { "Show me my cart", "Continue browsing", "Cancel" };

        sw.Stop();
        ctx.Steps.Add(new PipelineStep
        {
            Name = "4a. Action Trigger",
            Detail = $"published actionKey='{ctx.ActionKey}' to action-queue",
            ElapsedMs = sw.ElapsedMilliseconds
        });
    }

    private static string Humanise(string key) => key.Replace('_', ' ');
}

// ── 4b. LLM Generation (RAG) ─────────────────────────────────────────
public class LlmGenerationStep
{
    private readonly ExperionDbContext _db;
    private readonly ILlmProvider _llm;
    public LlmGenerationStep(ExperionDbContext db, ILlmProvider llm) { _db = db; _llm = llm; }

    public async Task RunAsync(PipelineContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Pull tenant KB
        var tenant = await _db.Tenants.FindAsync(new object?[] { ctx.Request.TenantId }, ct);
        var kb = tenant?.KbContent ?? "";

        // Naive retrieval: take KB sections (split by ##) whose tokens overlap the query.
        var sections = kb.Split("\n\n## ", StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.StartsWith("##") ? s : "## " + s)
                         .ToList();
        var qTokens = System.Text.RegularExpressions.Regex
            .Matches(ctx.NormalisedQuery.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value).Where(t => t.Length > 2).ToHashSet();

        var top = sections
            .Select(sec => new {
                Section = sec,
                Score = System.Text.RegularExpressions.Regex
                    .Matches(sec.ToLowerInvariant(), @"[a-z0-9]+")
                    .Count(m => qTokens.Contains(m.Value))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => x.Section)
            .ToList();

        ctx.KbContext = string.Join("\n\n", top);

        var systemPrompt = "You are Experion, a helpful AI assistant. Answer using the provided CONTEXT. " +
                           "If the context doesn't cover the question, say so honestly. Be concise.";
        var userPrompt =
            (string.IsNullOrEmpty(ctx.ConversationContext) ? "" : $"## HISTORY\n{ctx.ConversationContext}\n\n") +
            (string.IsNullOrEmpty(ctx.KbContext) ? "" : $"## CONTEXT\n{ctx.KbContext}\n\n") +
            $"## QUESTION\n{ctx.NormalisedQuery}";

        ctx.FinalAnswer = await _llm.CompleteAsync(systemPrompt, userPrompt, ct);

        // Generate suggestion chips from KB section headings
        ctx.Suggestions = top
            .Select(s => System.Text.RegularExpressions.Regex.Match(s, @"^##\s*(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => $"Tell me more about {s}")
            .Take(3)
            .ToList();

        sw.Stop();
        ctx.Steps.Add(new PipelineStep
        {
            Name = "4b. LLM Generation (RAG)",
            Detail = $"kb-sections={top.Count}, answer-len={ctx.FinalAnswer.Length}",
            ElapsedMs = sw.ElapsedMilliseconds
        });
    }
}

// ── 5. Persist & Learn ───────────────────────────────────────────────
public class PersistStep
{
    private readonly ExperionDbContext _db;
    public PersistStep(ExperionDbContext db) { _db = db; }

    public async Task RunAsync(PipelineContext ctx, long totalElapsedMs, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Conversation history — both turns
        _db.ConversationHistory.Add(new ConversationTurn
        {
            SessionId = ctx.Request.SessionId,
            UserId = ctx.UserId,
            Role = "user",
            Content = ctx.Request.UserMessage,
            IntentType = ctx.IntentType,
            ActionKey = ctx.ActionKey,
            CacheHit = ctx.CacheHit
        });
        _db.ConversationHistory.Add(new ConversationTurn
        {
            SessionId = ctx.Request.SessionId,
            UserId = ctx.UserId,
            Role = "assistant",
            Content = ctx.FinalAnswer,
            IntentType = ctx.IntentType,
            ActionKey = ctx.ActionKey,
            CacheHit = ctx.CacheHit
        });

        // Cache write-back (only on miss + GENERATION + no error)
        if (!ctx.CacheHit && ctx.IntentType == "GENERATION" && !string.IsNullOrEmpty(ctx.FinalAnswer))
        {
            _db.SemanticCache.Add(new SemanticCacheEntry
            {
                TenantId = ctx.Request.TenantId,
                NormalisedQuery = ctx.NormalisedQuery,
                Embedding = JsonSerializer.Serialize(ctx.Embedding),
                Answer = ctx.FinalAnswer,
                IntentType = ctx.IntentType
            });
        }

        // Audit
        _db.AuditLogs.Add(new AuditLog
        {
            LogId = ctx.LogId,
            SessionId = ctx.Request.SessionId,
            UserId = ctx.UserId,
            IntentType = ctx.IntentType,
            ActionKey = ctx.ActionKey,
            CacheHit = ctx.CacheHit,
            Prompt = ctx.NormalisedQuery,
            Response = ctx.FinalAnswer,
            ElapsedMs = totalElapsedMs
        });

        await _db.SaveChangesAsync(ct);

        sw.Stop();
        ctx.Steps.Add(new PipelineStep
        {
            Name = "5. Persist & Learn",
            Detail = ctx.CacheHit
                ? "wrote: history (2), audit (1)"
                : "wrote: history (2), audit (1), cache (1)",
            ElapsedMs = sw.ElapsedMilliseconds
        });
    }
}
