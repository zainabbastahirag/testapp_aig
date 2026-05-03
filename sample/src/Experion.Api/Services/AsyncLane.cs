using System.Threading.Channels;
using Experion.Api.Data;
using Experion.Api.Models;
using Experion.Api.Providers;
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

// ── Worker that consumes the action queue ──
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

                // Real implementation would call an internal API based on ActionMappings.TargetEndpoint.
                // We simulate side-effects by pushing a UI hint to the SDK over SignalR.
                using var scope = _sp.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ExperionHub>>();
                await hub.Clients.User(job.UserId).SendAsync("action_executed", new
                {
                    actionKey = job.ActionKey,
                    page = job.PageUrl,
                    timestamp = DateTime.UtcNow
                }, stoppingToken);

                await Task.Delay(50, stoppingToken); // simulate work
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[ActionWorker] failed for {Action}", job.ActionKey);
            }
        }
    }
}

// ── Activity event channel (in-memory = stand-in for events-queue) ──
public interface IActivityEventBus
{
    ValueTask PublishAsync(string sessionId, ActivityEventDto evt, CancellationToken ct = default);
    ChannelReader<(string SessionId, ActivityEventDto Event)> Reader { get; }
}

public class ChannelActivityBus : IActivityEventBus
{
    private readonly Channel<(string, ActivityEventDto)> _ch = Channel.CreateUnbounded<(string, ActivityEventDto)>();
    public ValueTask PublishAsync(string sessionId, ActivityEventDto evt, CancellationToken ct = default)
        => _ch.Writer.WriteAsync((sessionId, evt), ct);
    public ChannelReader<(string SessionId, ActivityEventDto Event)> Reader => _ch.Reader;
}

// ── Activity Mining Worker ──
// Consumes activity events, persists raw rows (in real arch: writes JSONL to Blob),
// updates UserProfile, and feeds the recommendation trigger engine.
public class ActivityMiningWorker : BackgroundService
{
    private readonly IActivityEventBus _bus;
    private readonly IServiceProvider _sp;
    private readonly ILogger<ActivityMiningWorker> _log;

    public ActivityMiningWorker(IActivityEventBus bus, IServiceProvider sp, ILogger<ActivityMiningWorker> log)
    {
        _bus = bus; _sp = sp; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (sessionId, evt) in _bus.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ExperionDbContext>();
                var reco = scope.ServiceProvider.GetRequiredService<RecommendationEngine>();

                var userId = sessionId.Contains("__") ? sessionId.Split("__")[0] : sessionId;

                db.ActivityEvents.Add(new ActivityEventRecord
                {
                    SessionId = sessionId,
                    UserId = userId,
                    Type = evt.Type,
                    Url = evt.Url,
                    Selector = evt.Selector,
                    Text = evt.Text,
                    MetaJson = evt.Meta == null ? null : System.Text.Json.JsonSerializer.Serialize(evt.Meta),
                    Timestamp = evt.Timestamp
                });

                var profile = await db.UserProfiles.FindAsync(new object[] { userId }, stoppingToken);
                if (profile == null)
                {
                    profile = new UserProfile
                    {
                        UserId = userId,
                        IsAnonymous = userId.StartsWith("anon-", StringComparison.OrdinalIgnoreCase),
                        TenantId = "default"
                    };
                    db.UserProfiles.Add(profile);
                }
                profile.TotalEvents += 1;
                profile.LastSeenAt = DateTime.UtcNow;

                await db.SaveChangesAsync(stoppingToken);

                // Trigger evaluation
                await reco.EvaluateAsync(userId, sessionId, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[ActivityMiningWorker] failed processing event");
            }
        }
    }
}

// ── Recommendation Trigger Engine ──
public class RecommendationEngine
{
    private readonly ExperionDbContext _db;
    private readonly ILlmProvider _llm;
    private readonly IHubContext<ExperionHub> _hub;
    private readonly ILogger<RecommendationEngine> _log;

    public RecommendationEngine(ExperionDbContext db, ILlmProvider llm, IHubContext<ExperionHub> hub,
        ILogger<RecommendationEngine> log)
    {
        _db = db; _llm = llm; _hub = hub; _log = log;
    }

    public async Task EvaluateAsync(string userId, string sessionId, CancellationToken ct)
    {
        var profile = await _db.UserProfiles.FindAsync(new object?[] { userId }, ct);
        if (profile == null) return;

        var tenant = await _db.Tenants.FindAsync(new object?[] { profile.TenantId }, ct) ?? new TenantConfig();
        var cooldown = TimeSpan.FromSeconds(tenant.NudgeCooldownSeconds);
        if (DateTime.UtcNow - profile.LastNudgeAt < cooldown) return;

        // Rule: if user produced >= 5 events in the last 30s, nudge.
        var recent = await _db.ActivityEvents
            .Where(e => e.UserId == userId && e.Timestamp >= DateTime.UtcNow.AddSeconds(-30))
            .OrderByDescending(e => e.Timestamp)
            .Take(20)
            .ToListAsync(ct);
        if (recent.Count < 5) return;

        // Build a tiny activity summary for the LLM
        var summary = string.Join("\n", recent.Take(8).Select(e => $"- {e.Type} {e.Url} {e.Text}"));

        var nudge = await _llm.CompleteAsync(
            systemPrompt: "{\"task\":\"draft_recommendation\"}  You are Experion. Based on the user's recent activity, draft a short proactive helper message (max 2 sentences) and 3 quick reply suggestions.",
            userPrompt:   $"## RECENT_ACTIVITY\n{summary}\n\n## QUESTION\nWhat could I helpfully suggest right now?",
            ct);

        var payload = new NudgePayload
        {
            Type = "nudge",
            Message = nudge,
            Suggestions = new List<string> { "Yes please", "Show me alternatives", "Not now" },
            Reason = $"{recent.Count} events in last 30s"
        };

        profile.LastNudgeAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("[Reco] pushing nudge to user {User}", userId);
        await _hub.Clients.User(userId).SendAsync("nudge", payload, ct);
    }
}

// ── SignalR hub ──
public class ExperionHub : Hub
{
    public override Task OnConnectedAsync()
    {
        // The SDK passes the userId as ?userId=... when connecting.
        return base.OnConnectedAsync();
    }
}

public class UserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var http = connection.GetHttpContext();
        return http?.Request.Query["userId"].FirstOrDefault();
    }
}
