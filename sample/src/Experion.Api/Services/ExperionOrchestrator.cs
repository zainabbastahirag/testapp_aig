using System.Diagnostics;
using Experion.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Experion.Api.Services;

public interface IExperionService
{
    Task<ProcessResponse> ProcessAsync(ProcessRequest request, CancellationToken ct = default);
    Task RecordFeedbackAsync(FeedbackRequest req, CancellationToken ct = default);
}

public class ExperionOrchestrator : IExperionService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ExperionOrchestrator> _log;

    public ExperionOrchestrator(IServiceProvider sp, ILogger<ExperionOrchestrator> log)
    {
        _sp = sp;
        _log = log;
    }

    public async Task<ProcessResponse> ProcessAsync(ProcessRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var scope = _sp.CreateScope();
        var ctxBuilder = scope.ServiceProvider.GetRequiredService<ContextBuilderStep>();
        var cacheStep  = scope.ServiceProvider.GetRequiredService<SemanticCacheStep>();
        var router     = scope.ServiceProvider.GetRequiredService<IntentRouterStep>();
        var actionStep = scope.ServiceProvider.GetRequiredService<ActionTriggerStep>();
        var llmStep    = scope.ServiceProvider.GetRequiredService<LlmGenerationStep>();
        var persist    = scope.ServiceProvider.GetRequiredService<PersistStep>();

        var ctx = new PipelineContext { Request = request };

        try
        {
            await ctxBuilder.RunAsync(ctx, ct);
            await cacheStep.RunAsync(ctx, ct);

            if (ctx.CacheHit)
            {
                ctx.FinalAnswer = ctx.CachedAnswer ?? "";
                ctx.Suggestions = new List<string> { "Tell me more", "Show related", "Talk to a human" };
            }
            else
            {
                await router.RunAsync(ctx, ct);

                if (ctx.IntentType == "ACTION")
                    await actionStep.RunAsync(ctx, ct);
                else
                    await llmStep.RunAsync(ctx, ct);
            }

            sw.Stop();
            await persist.RunAsync(ctx, sw.ElapsedMilliseconds, ct);

            return new ProcessResponse
            {
                Success = true,
                LogId = ctx.LogId,
                IntentType = ctx.IntentType,
                ActionKey = ctx.ActionKey,
                Message = ctx.FinalAnswer,
                Suggestions = ctx.Suggestions,
                CacheHit = ctx.CacheHit,
                ProcessingMs = sw.ElapsedMilliseconds,
                Pipeline = ctx.Steps
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "[Experion] pipeline failed for log {LogId}", ctx.LogId);
            return new ProcessResponse
            {
                Success = false,
                LogId = ctx.LogId,
                ProcessingMs = sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message,
                Pipeline = ctx.Steps
            };
        }
    }

    public async Task RecordFeedbackAsync(FeedbackRequest req, CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.ExperionDbContext>();
        var entry = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.AuditLogs.Where(a => a.LogId == req.LogId), ct);
        if (entry != null)
        {
            entry.FeedbackPositive = req.Positive;
            entry.FeedbackComment = req.Comment;
            await db.SaveChangesAsync(ct);
        }
    }
}
