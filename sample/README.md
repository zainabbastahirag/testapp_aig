# Experion — runnable sample

Self-contained reference implementation of the Experion architecture.

- **.NET 8 Web API** with **two main endpoints** + a **flat ExperionService** (one method per pipeline step)
- **Drop-in JS SDK** (`wwwroot/experion.js`)
- **Sample HTML page** with one-click buttons for every flow + live data inspectors (`wwwroot/index.html`)
- **Real Blob Storage** for conversation history + activity events (Azure Blob Append Blobs in production, JSONL files locally for the demo)
- **SQL Server** (SQLite locally) for ActionMappings, SemanticCache, UserProfile, AuditLog, TenantConfig
- **Mock LLM/embedding** by default → swap to real Azure OpenAI with one config switch

## Run

```bash
cd sample/src/Experion.Api
dotnet run
```

Open <http://localhost:5077/index.html>.

## The two endpoints

This is the whole public surface for the agent loop:

```
POST /api/experion/track     ← passive activity events
POST /api/experion/process   ← chat / action / generation
```

Everything else (`/identify`, `/config/{t}`, `/feedback`, `/inspect/*`) is supporting plumbing.

## ExperionController — flat & boring on purpose

```csharp
// FUNCTION 1 — Track Activity (passive)
TrackActivity(req)
    -> _service.TrackActivityAsync(...)         // append events to Blob + update profile
    -> _reco.EvaluateAsync(...)                 // maybe push a nudge

// FUNCTION 2 — Process (chat / action / generation)
Process(req)
    var embedding = await _service.EmbedQueryAsync(query);
    var cache     = await _service.CheckSemanticCacheAsync(tenant, embedding);
    if (cache.Hit) {
        answer     = use cached;
        intentType = cache.IntentType;
    } else {
        var intent = await _service.ClassifyIntentAsync(tenant, query, embedding);
        if (intent.IntentType == "ACTION")
            answer = await _service.RunActionAsync(req, userId, intent.ActionKey);
        else
            answer = await _service.RunGenerationAsync(tenant, sid, userId, query);
    }
    await _service.PersistAsync(req, userId, logId, intentType, actionKey,
                                cache.Hit, embedding, answer, elapsedMs);
    return ProcessResponse;
```

That's the whole pipeline — top to bottom in `Controllers/ExperionController.cs`. Every step is one method on `ExperionService`. No nested orchestrators, no per-step classes.

## ExperionService — flat methods only

`Services/ExperionService.cs` exposes one public method per step, all at the same level:

| Method | Step | What it does |
|---|---|---|
| `TrackActivityAsync` | /track | Appends events to Blob (JSONL), updates `UserProfile` row |
| `EmbedQueryAsync` | 1 | Calls embedding provider → `float[]` |
| `CheckSemanticCacheAsync` | 2 | Cosine vs. `SemanticCache`. Returns `{ Hit, Score, Answer, IntentType }` |
| `ClassifyIntentAsync` | 3 | Hybrid (embedding + phrase) match vs. `ActionMappings`. Returns `{ IntentType, ActionKey?, scores }` |
| `RunActionAsync` | 4a | Dispatches an `ActionJob` to the action queue |
| `RunGenerationAsync` | 4b | Reads conversation history from Blob, retrieves KB, calls LLM |
| `PersistAsync` | 5 | Writes 2 JSONL lines to Blob, plus `SemanticCache` (on miss) + `AuditLog` to SQL |
| `ReadConversationTailAsync` | helper | Used by step 4b and inspector endpoint |
| `ListUserBlobsAsync` | helper | Used by inspector endpoint |
| `ReadBlobLinesAsync` | helper | Used by inspector endpoint |

## Where things live

| Data | Storage |
|---|---|
| **Conversation history** | **Blob** — `conversations/{tenant}/{userId}/{yyyy-MM-dd}.jsonl` (one JSONL line per turn) |
| **Activity events** | **Blob** — `activity/{tenant}/{userId}/{yyyy-MM-dd}.jsonl` |
| Action mappings | SQL — `ActionMappings` |
| Semantic cache | SQL — `SemanticCache` |
| User profile | SQL — `UserProfile` |
| Audit log | SQL — `AuditLog` |
| Tenant config | SQL — `TenantConfig` |

Conversation and activity are append-only and don't need indexed lookups — perfect fit for **Azure Append Blobs**. The other tables need fast indexed reads (cache hit lookup, action mapping lookup, profile-by-userId), so they live in SQL.

## Blob storage — real implementation

`Storage/IBlobStore.cs` has two implementations:

| Class | Used when |
|---|---|
| `LocalFileBlobStore` | `BlobStorage:ConnectionString` is empty (default) — writes JSONL files under `./data/blob/...` |
| `AzureBlobStore` | `BlobStorage:ConnectionString` is set — uses `Azure.Storage.Blobs` with **Append Blobs** (`AppendBlobClient.AppendBlockAsync`), perfect for concurrent appends without read-modify-write |

To switch to real Azure:

```json
// appsettings.json
{
  "BlobStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  }
}
```

That's the only change required — the service code is identical.

## Switch to Azure OpenAI

```json
{
  "Llm": { "Provider": "AzureOpenAI" },
  "AzureOpenAI": {
    "Endpoint": "https://<resource>.openai.azure.com",
    "ApiKey": "<key>",
    "ChatDeployment": "gpt-4.1",
    "EmbeddingDeployment": "text-embedding-3-large",
    "ApiVersion": "2024-08-01-preview"
  }
}
```

Delete `experion.db` to re-seed `ActionMappings` with real embeddings.

## API quick reference

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/experion/track` | **Function 1** — track activity events |
| `POST` | `/api/experion/process` | **Function 2** — chat / action / generation |
| `POST` | `/api/experion/identify` | Mint session id |
| `GET`  | `/api/experion/config/{tenant}` | Tenant config for SDK boot |
| `POST` | `/api/experion/feedback` | Thumbs up/down |
| `GET`  | `/api/experion/inspect/conversation/{userId}?tenantId=...` | Tail conversation blob |
| `GET`  | `/api/experion/inspect/blobs/{userId}?tenantId=...` | List user's blob paths |
| `GET`  | `/api/experion/inspect/blob?container=...&path=...` | Read specific blob |
| `GET`  | `/api/experion/inspect/cache` / `/audit` / `/profile/{u}` / `/actions` | Inspectors |
| `WS`   | `/hubs/experion?userId=<id>` | SignalR for nudges + action echoes |

## Files

```
sample/src/Experion.Api/
├── Controllers/ExperionController.cs    ← 2 main endpoints
├── Services/
│   ├── ExperionService.cs               ← FLAT, one method per step
│   └── AsyncLane.cs                     ← Action queue + worker, RecoEngine, SignalR hub
├── Storage/
│   ├── IBlobStore.cs                    ← interface
│   ├── LocalFileBlobStore.cs            ← demo / offline
│   ├── AzureBlobStore.cs                ← real Azure (Append Blobs)
│   └── BlobPaths.cs                     ← centralised path conventions
├── Providers/
│   ├── Interfaces.cs
│   ├── MockProviders.cs                 ← default
│   └── AzureOpenAIProviders.cs          ← real
├── Data/
│   ├── ExperionDbContext.cs             ← SQL only — cache/audit/profile/actions/tenant
│   ├── Entities.cs
│   └── Seeder.cs                        ← seeds 6 ActionMappings + 1 tenant
├── Models/Dtos.cs
├── Program.cs
├── appsettings.json
└── wwwroot/
    ├── experion.js                      ← drop-in SDK
    └── index.html                       ← sample test page
```
