# Experion — runnable sample

A self-contained implementation of the Experion architecture from `docs/architecture/`:

- **.NET 8 Web API** (`src/Experion.Api`) — orchestrator + pipeline + async lane + SignalR
- **Drop-in JS SDK** (`wwwroot/experion.js`) — identity, activity tracker, gesture, idle trigger, sphere + sidebar UI, SignalR client
- **Sample HTML page** (`wwwroot/index.html`) — exercises every flow with one-click buttons + live data inspectors

The sample uses **SQLite** for storage and a **mock LLM/embedding provider** by default so it runs without any Azure keys. To use real Azure OpenAI, see [Switching to Azure OpenAI](#switching-to-azure-openai).

## Run it

```bash
cd sample/src/Experion.Api
dotnet run
```

Then open <http://localhost:5077/index.html>.

You'll see the floating sphere bottom-right (the SDK is loaded by the page itself). The page has six numbered sections, each one walks you through a part of the flow:

1. **SDK boot & identity** — shows the session, resolved user id, region cluster, tenant config; SignalR connection status.
2. **Action Trigger path** — clickable buttons whose text matches `ActionMappings` rows. Watch the sidebar pipeline view + the toast that appears when the `ActionWorker` consumes the queue and echoes back via SignalR.
3. **LLM Generation path** — clickable KB questions. First call is a **cache MISS** (you'll see all 5 pipeline steps). Repeat the same one — second call is a **cache HIT** (just steps 1, 2, 5 — no LLM).
4. **Circle gesture** — hold **Alt** and drag to circle a region. The SDK extracts DOM text inside the circle and sends it as `capturedText`.
5. **Proactive recommendation** — fires 6 events; the `ActivityMiningWorker` triggers the `RecommendationEngine`, which drafts a nudge and pushes it back via SignalR.
6. **Inspect the data layer** — buttons that read `ConversationHistory`, `SemanticCache`, `AuditLog`, `UserProfile`, `ActionMappings`.

## What's implemented (matches the architecture diagram)

| Layer | What you'll find |
|---|---|
| **SDK** | `Experion.Identity`, `Experion.ActivityTracker` (passive, batched), `Experion.Gesture` (Alt+drag), `Experion.Idle`, `Experion.UI` (sphere + sidebar + toast), SignalR client |
| **API** | `ExperionController` with `/identify`, `/config/{tenant}`, `/events`, `/process`, `/feedback`, `/inspect/*`, plus the `/hubs/experion` SignalR hub |
| **Orchestrator** | `ExperionOrchestrator` composes 5 pipeline steps |
| **Pipeline** | `ContextBuilderStep` → `SemanticCacheStep` → (HIT short-circuits) → `IntentRouterStep` → (`ActionTriggerStep` OR `LlmGenerationStep`) → `PersistStep` |
| **Async lane** | `ChannelActivityBus` (= Service Bus events-queue) → `ActivityMiningWorker` → `RecommendationEngine` → SignalR push. `ChannelActionDispatcher` (= Service Bus action-queue) → `ActionWorker` → SignalR push |
| **Storage** | EF Core SQLite: `ConversationHistory`, `ActionMappings`, `SemanticCache`, `UserProfile`, `ActivityEvents`, `AuditLogs`, `Tenants` |
| **Providers** | `IEmbeddingProvider` and `ILlmProvider` interfaces with `Mock*` (default) and `AzureOpenAI*` implementations |

### Pipeline response shape

Every `/process` response includes a `pipeline` array showing exactly which steps ran, what they decided, and how long they took — that's why the sidebar can render the step trace under each AI message.

```json
{
  "intentType": "GENERATION",
  "cacheHit": true,
  "processingMs": 4,
  "pipeline": [
    { "name": "1. Context Builder",  "elapsedMs": 1, "detail": "user=anon-…, history-turns=2" },
    { "name": "2. Semantic Cache",   "elapsedMs": 2, "detail": "HIT (cosine=1.000) — short-circuiting LLM" },
    { "name": "5. Persist & Learn",  "elapsedMs": 1, "detail": "wrote: history (2), audit (1)" }
  ]
}
```

### Intent router

The `IntentRouterStep` uses a **hybrid** signal: cosine similarity between the embedded query and each `ActionMappings.Embedding`, **plus** Jaccard / substring match against `ActionMappings.Phrases`. Either signal above threshold (or a weighted hybrid above the hybrid threshold) classifies the request as `ACTION`. This mirrors how production systems combine semantic + lexical signals to avoid embedding model misses on short imperative phrases.

## API quick reference

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/experion/identify` | Mint a session id (UserID or anonId + IP/region cluster) |
| `GET`  | `/api/experion/config/{tenantId}` | Tenant config for SDK boot |
| `POST` | `/api/experion/events` | Async batched activity ingest |
| `POST` | `/api/experion/process` | Sync entry point — runs the full pipeline |
| `POST` | `/api/experion/feedback` | Thumbs up/down on a previous answer |
| `GET`  | `/api/experion/inspect/history/{sessionId}` | Conversation history |
| `GET`  | `/api/experion/inspect/cache` | Semantic cache rows |
| `GET`  | `/api/experion/inspect/audit` | Audit log rows |
| `GET`  | `/api/experion/inspect/profile/{userId}` | User profile + recent events |
| `GET`  | `/api/experion/inspect/actions` | Configured action mappings |
| `WS`   | `/hubs/experion?userId=<id>` | SignalR hub for nudges + action echoes |

## Switching to Azure OpenAI

Edit `appsettings.json`:

```json
{
  "Llm": { "Provider": "AzureOpenAI" },
  "AzureOpenAI": {
    "Endpoint": "https://<your-resource>.openai.azure.com",
    "ApiKey": "<your-key>",
    "ChatDeployment": "gpt-4.1",
    "EmbeddingDeployment": "text-embedding-3-large",
    "ApiVersion": "2024-08-01-preview"
  }
}
```

The `AzureOpenAIEmbeddingProvider` and `AzureOpenAILlmProvider` will be wired in instead of the mocks. Delete `experion.db` to re-seed `ActionMappings` with the real embeddings.

## Mapping back to the architecture diagram

| Diagram element | Code |
|---|---|
| Frontend SDK — Identity / Tracker / Gesture / Idle / UI | `wwwroot/experion.js` |
| `ExperionController` (API) | `Controllers/ExperionController.cs` |
| `IExperionService` orchestrator | `Services/ExperionOrchestrator.cs` |
| Step 1 — Context Builder | `Services/PipelineSteps.cs#ContextBuilderStep` |
| Step 2 — Semantic Cache | `Services/PipelineSteps.cs#SemanticCacheStep` |
| Step 3 — NLP Intent Router | `Services/PipelineSteps.cs#IntentRouterStep` |
| Step 4a — Action Trigger | `Services/PipelineSteps.cs#ActionTriggerStep` |
| Step 4b — LLM Generation (RAG) | `Services/PipelineSteps.cs#LlmGenerationStep` |
| Step 5 — Persist & Learn | `Services/PipelineSteps.cs#PersistStep` |
| Service Bus action-queue | `Services/AsyncLane.cs#ChannelActionDispatcher` |
| Service Bus events-queue | `Services/AsyncLane.cs#ChannelActivityBus` |
| Activity Mining Worker | `Services/AsyncLane.cs#ActivityMiningWorker` |
| Recommendation Trigger Engine | `Services/AsyncLane.cs#RecommendationEngine` |
| SignalR push | `Services/AsyncLane.cs#ExperionHub` |
| `ActionMappings` SQL table | `Data/Entities.cs#ActionMapping` (seeded by `Data/Seeder.cs`) |
| `ConversationHistory` SQL table | `Data/Entities.cs#ConversationTurn` |
| `SemanticCache` index/table | `Data/Entities.cs#SemanticCacheEntry` |
| `UserProfile` table | `Data/Entities.cs#UserProfile` |
| Tenant config | `Data/Entities.cs#TenantConfig` |
| Azure OpenAI | `Providers/AzureOpenAIProviders.cs` (or `Providers/MockProviders.cs` for offline) |

## Production swap-in checklist

Each in-memory / mock piece below has an obvious production replacement:

| Demo | Production |
|---|---|
| `MockLlmProvider` / `MockEmbeddingProvider` | `AzureOpenAILlmProvider` / `AzureOpenAIEmbeddingProvider` (already in repo) |
| `ChannelActivityBus` / `ChannelActionDispatcher` | Azure Service Bus topics/queues |
| Per-tenant KB stored in `TenantConfig.KbContent` + naive keyword retrieval | Azure AI Search KB index + hybrid retrieval |
| `SemanticCacheEntry` table | Azure AI Search "semantic cache" vector index (with SQL fallback) |
| Raw activity events in `ActivityEvents` table | Blob Storage JSONL, partitioned `tenant/userId/yyyymmdd.jsonl` |
| SQLite | SQL Server |
| In-process SignalR | Azure SignalR Service |
| In-process scoped audit | Application Insights + audit table |
