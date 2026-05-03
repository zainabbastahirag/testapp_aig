using System.Text.Json;
using System.Threading.Channels;
using Experion.Api.Data;
using Experion.Api.Models;
using Experion.Api.Providers;
using Experion.Api.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Experion.Api.Services;

// ── Action queue (in-memory channel = stand-in for Service Bus action-queue) ──

public class ActionJob
{
    public string ActionKey { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string Query { get; set; } = string.Empty;
}

public interface IActionDispatcher
{
    ValueTask DispatchAsync(ActionJob job, CancellationToken ct = default);
    ChannelReader<ActionJob> Reader { get; }
}

public class ChannelActionDispatcher : IActionDispatcher
{
    private readonly Channel<ActionJob> _channel = Channel.CreateUnbounded<ActionJob>();
    public ValueTask DispatchAsync(ActionJob job, CancellationToken ct = default) => _channel.Writer.WriteAsync(job, ct);
    public ChannelReader<ActionJob> Reader => _channel.Reader;
}

// ── Action worker (consumes action-queue, executes, echoes via SignalR) ──

public class ActionWorker : BackgroundService
{
    private readonly IActionDispatcher _dispatcher;
    private readonly IServiceProvider _sp;
    private readonly ILogger<ActionWorker> _log;

    public ActionWorker(IActionDispatcher d, IServiceProvider sp, ILogger<ActionWorker> log)
    {
        _dispatcher = d; _sp = sp; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _dispatcher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _log.LogInformation("[ActionWorker] executing {Action} for {User} on {Url}",
                    job.ActionKey, job.UserId, job.PageUrl);

                using var scope = _sp.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ExperionHub>>();
                await hub.Clients.User(job.UserId).SendAsync("action_executed", new
                {
                    actionKey = job.ActionKey,
                    page = job.PageUrl,
                    timestamp = DateTime.UtcNow
                }, stoppingToken);

                await Task.Delay(50, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogError(ex, "[ActionWorker] failed for {Action}", job.ActionKey); }
        }
    }
}

// ── Recommendation engine ──
// Triggered by the controller (or a timer) after activity is tracked.
// Reads recent activity from blob, drafts a nudge with the LLM, pushes via SignalR.

public class RecommendationEngine
{
    private readonly ExperionDbContext _db;
    private readonly IBlobStore _blob;
    private readonly ILlmProvider _llm;
    private readonly IHubContext<ExperionHub> _hub;
    private readonly ILogger<RecommendationEngine> _log;

    public const int MinEventsForNudge = 5;

    public RecommendationEngine(
        ExperionDbContext db, IBlobStore blob, ILlmProvider llm,
        IHubContext<ExperionHub> hub, ILogger<RecommendationEngine> log)
    {
        _db = db; _blob = blob; _llm = llm; _hub = hub; _log = log;
    }

    public async Task EvaluateAsync(string tenantId, string userId, CancellationToken ct)
    {
        var profile = await _db.UserProfiles.FindAsync(new object?[] { userId }, ct);
        if (profile == null) return;

        var tenant = await _db.Tenants.FindAsync(new object?[] { tenantId }, ct) ?? new TenantConfig();
        var cooldown = TimeSpan.FromSeconds(tenant.NudgeCooldownSeconds);
        if (DateTime.UtcNow - profile.LastNudgeAt < cooldown) return;

        var path = BlobPaths.Activity(tenantId, userId, DateTime.UtcNow);
        var lines = await _blob.ReadTailAsync(BlobPaths.ActivityContainer, path, 20, ct);
        if (lines.Count < MinEventsForNudge) return;

        var summary = string.Join("\n", lines.TakeLast(8).Select(l =>
        {
            try
            {
                var d = JsonDocument.Parse(l).RootElement;
                return $"- {Get(d, "Type")} {Get(d, "Url")} {Get(d, "Text")}";
            }
            catch { return "- (parse error)"; }
        }));

        var nudge = await _llm.CompleteAsync(
            systemPrompt: "{\"task\":\"draft_recommendation\"}  You are Experion. Based on the user's recent activity, " +
                          "draft a short proactive helper message (max 2 sentences).",
            userPrompt:   $"## RECENT_ACTIVITY\n{summary}\n\n## QUESTION\nWhat could I helpfully suggest right now?",
            ct);

        var payload = new NudgePayload
        {
            Type = "nudge",
            Message = nudge,
            Suggestions = new List<string> { "Yes please", "Show me alternatives", "Not now" },
            Reason = $"{lines.Count} events in last batch"
        };

        profile.LastNudgeAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("[Reco] pushing nudge to {User}", userId);
        await _hub.Clients.User(userId).SendAsync("nudge", payload, ct);
    }

    private static string Get(JsonElement d, string name) =>
        d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

// ── SignalR hub ──

public class ExperionHub : Hub { }

public class UserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var http = connection.GetHttpContext();
        return http?.Request.Query["userId"].FirstOrDefault();
    }
}
