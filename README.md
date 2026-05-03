# AG ONE Experion — Architecture & Redesign

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        ANY Website / Web App                            │
│                                                                         │
│   ┌────────────────────────────────────────────────────────────────┐   │
│   │                    experion.js  (single script tag)            │   │
│   │                                                                │   │
│   │  ┌─────────────┐  ┌──────────────┐  ┌───────────────────────┐ │   │
│   │  │Activity Miner│  │GestureDetector│  │   Sidebar / UI        │ │   │
│   │  │ - page views │  │  Alt+Drag    │  │  - Chat messages      │ │   │
│   │  │ - clicks     │  │  circle drawn│  │  - Recommendations    │ │   │
│   │  │ - searches   │  │  DOM extract │  │  - Proactive nudges   │ │   │
│   │  │ - scrolls    │  └──────┬───────┘  └──────────────────────-┘ │   │
│   │  └──────┬───────┘         │                                     │   │
│   └─────────│─────────────────│─────────────────────────────────────┘   │
└─────────────│─────────────────│─────────────────────────────────────────┘
              │                 │
     POST /api/activity/track   POST /api/experion/process
              │                 │                POST /api/recommend
              ▼                 ▼                       ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         AGOneExperion.API                               │
│                                                                         │
│   ActivityController   ExperionController    RecommendController        │
│   POST /track          POST /process         POST /recommend            │
│                        POST /ask             (proactive agent)          │
│                        POST /feedback                                   │
│                        GET  /health                                     │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │  IExperionOrchestrator
┌────────────────────────────────▼────────────────────────────────────────┐
│                      AGOneExperion.Infrastructure                       │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                   ExperionOrchestrator                          │   │
│  │                                                                 │   │
│  │  Activity Mining Path:                                          │   │
│  │   SDK events → UserIdentityResolver → BlobActivityRepository   │   │
│  │               → ActivityProfileBuilder → Blob (profile.json)   │   │
│  │                                                                 │   │
│  │  Circle Gesture Path:                                           │   │
│  │   DOM fragment → DomFragmentCleaner                            │   │
│  │   → LLM #1: context detection + search query                   │   │
│  │   → AzureSearchService (hybrid: keyword + vector)              │   │
│  │   → LLM #2: RAG response generation                            │   │
│  │   → LlmRecommendationEngine → ExperionResponse                 │   │
│  │                                                                 │   │
│  │  Recommendation Path:                                           │   │
│  │   BlobActivityRepository.GetProfile                            │   │
│  │   → LlmRecommendationEngine (profile + page context)           │   │
│  │   → RecommendationResponse                                     │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌──────────────────┐  ┌────────────────────┐  ┌──────────────────┐   │
│  │BlobActivityRepo  │  │AzureOpenAIService   │  │AzureSearchService│   │
│  │                  │  │                    │  │                  │   │
│  │Azure Blob Storage│  │Chat completions    │  │Hybrid search     │   │
│  │ experion-activity│  │Embeddings          │  │Keyword + Vector  │   │
│  │ experion-profiles│  │(gpt-4.1, emb-3)    │  │Azure AI Search   │   │
│  └──────────────────┘  └────────────────────┘  └──────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

## Key Design Decisions vs. Previous Version

| Concern | Before | Now |
|---|---|---|
| **Architecture** | Single monolith mixed with AGONEAIHub | Clean standalone solution |
| **Activity Mining** | None | Full SDK-side capture + Blob persistence |
| **User Identity** | Not handled | IP-fingerprint for anonymous, pass-through for authenticated |
| **Recommendations** | None | LLM-powered engine reading user profile |
| **Proactive nudges** | None | Triggered after N events, before the user asks |
| **KB search** | Keyword only | Hybrid (keyword + vector embeddings) |
| **LLM calls** | 2 (detect + respond) | 2 for circle, 1 for recommend, 1 for ask |
| **Data store** | SQL Server + EF Core | Azure Blob Storage (no DB needed) |
| **Deployability** | Coupled to AGONEAIHub | Self-contained — one API, one JS file |

## Projects

```
AGOneExperion.sln
├── src/AGOneExperion.Core          — Models, Interfaces, Enums, Configuration
├── src/AGOneExperion.Infrastructure — Services, Orchestrator, Blob, AI, Search
└── src/AGOneExperion.API           — HTTP Controllers, Middleware, Program.cs
experion.js                         — Standalone SDK (single file, no dependencies)
```

## Quick Start

### 1. Configure `appsettings.json`

Fill in your Azure credentials in `src/AGOneExperion.API/appsettings.json`:

```json
{
  "Experion": {
    "BlobConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;",
    "OpenAiEndpoint": "https://YOUR_RESOURCE.openai.azure.com/",
    "OpenAiApiKey": "...",
    "ChatModel": "gpt-4.1",
    "EmbeddingModel": "text-embedding-3-small",
    "SearchEndpoint": "https://YOUR_SEARCH.search.windows.net",
    "SearchApiKey": "...",
    "DefaultKbIndex": "experion-kb"
  }
}
```

### 2. Run the API

```bash
cd src/AGOneExperion.API
dotnet run
# Swagger UI: http://localhost:5000
```

### 3. Add SDK to any website

```html
<script src="https://your-cdn.com/experion.js"
        data-api-url="https://your-api.azurewebsites.net"
        data-site-id="my-marketing-site"
        data-user-id=""
        data-kb-index="experion-kb"
        data-gesture="alt+drag"
        data-nudge-events="8"
        data-auto-init="true">
</script>
```

That's it. No other dependencies, no framework required.

## API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/activity/track` | Ingest activity event batch from SDK |
| `POST` | `/api/experion/process` | Circle gesture → contextual AI response |
| `POST` | `/api/experion/ask` | Follow-up chat question |
| `POST` | `/api/experion/feedback` | Thumbs up/down |
| `GET`  | `/api/experion/health` | SDK connectivity check |
| `POST` | `/api/recommend` | Get proactive recommendations for a user |

## Data Flow: Activity Mining

```
User visits page
  → SDK fires SessionStart + PageView events
  → Every 3 seconds: events POSTed to /api/activity/track
  → API: UserIdentityResolver resolves userId (or anon-{hash})
  → BlobActivityRepository appends to {userId}/events.ndjson
  → ActivityProfileBuilder.Merge updates {userId}/profile.json
  → After 8 events: SDK calls /api/recommend
  → LlmRecommendationEngine reads profile + current page
  → LLM generates ranked recommendations
  → Nudge toast appears in bottom-right corner
```

## Data Flow: Circle Gesture

```
User holds Alt and draws a circle
  → GestureDetector extracts DOM inside bounding box
  → POST /api/experion/process
  → DomFragmentCleaner normalises DOM text
  → LLM #1: detect context + generate KB search query
  → AzureSearchService: hybrid keyword+vector search
  → LLM #2: RAG response with suggestions
  → LlmRecommendationEngine: personalised recommendations
  → Response rendered in Sidebar
```

## Blob Storage Layout

```
Container: experion-activity
  └── {userId}/events.ndjson        (append blob, one JSON line per event)

Container: experion-profiles
  └── {userId}/profile.json         (overwritten on each session update)
```

Anonymous users get a stable ID: `anon-{SHA256(IP|UserAgent)[0:16]}`.
